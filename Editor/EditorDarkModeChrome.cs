// Ported from UnityEditor-DarkMode (https://github.com/0x7c13/UnityEditor-DarkMode):
//
// MIT License
//
// Copyright (c) 2024 Jiaqi (0x7c13) Liu
//
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in all
// copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
// SOFTWARE.

#if UNITY_EDITOR_WIN

#region

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using UnityEditor;
using UnityEngine;

#endregion

/// <summary>
///     Dark Windows title bars, menu bar, context menus and native dialogs for the Unity Editor (Windows 10 1903+ / 11).
///     Follows the editor skin: on while the Dark skin is active, off under Light, and the menu checkbox switches the
///     whole thing on or off.
///     C# port of UnityEditor-DarkMode by Jiaqi (0x7c13) Liu (MIT, https://github.com/0x7c13/UnityEditor-DarkMode),
///     itself based on ReaperThemeHackDll by jjYBdx4IL. Unlike the native DLL it can be switched off at runtime. Its
///     window subclasses call back into managed code, so they are removed before every domain reload and reinstalled
///     after it.
/// </summary>
[InitializeOnLoad]
public static class EditorDarkModeChrome
{
    const string MenuPath = "ColdSnap/Tools/Dark Title Bar and Menus";
    const string EnabledKey = "ColdSnap.EditorDarkModeChrome.Enabled";
    const string NativeModWarnedKey = "ColdSnap.EditorDarkModeChrome.NativeModWarned";
    const string NativeModDll = "UnityEditorDarkMode.dll";

    // Colours from the original mod's UnityEditorDarkMode.dll.ini defaults.
    static readonly uint TextColor = Rgb(200, 200, 200);
    static readonly uint TextColorDisabled = Rgb(160, 160, 160);
    static readonly uint MenuBarColor = Rgb(48, 48, 48);
    static readonly uint MenuItemColor = Rgb(48, 48, 48);
    static readonly uint MenuItemHotColor = Rgb(62, 62, 62);
    static readonly uint MenuItemSelectedColor = Rgb(62, 62, 62);

    static readonly UIntPtr SubclassId = (UIntPtr)0xC01D5;
    static readonly SubclassProc SubclassCallback = WndProc;
    static readonly HookProc CbtCallback = CbtProc;
    static readonly EnumWindowsProc AttachCallback = (hWnd, _) => {
        Attach(hWnd, Classify(hWnd));
        return true;
    };

    static readonly HashSet<IntPtr> Subclassed = new HashSet<IntPtr>();
    static readonly StringBuilder TextBuffer = new StringBuilder(256);

    static bool active;
    static bool? supported;
    static IntPtr cbtHook;
    static IntPtr menuTheme;
    static IntPtr menuBarBrush, menuItemBrush, menuItemHotBrush, menuItemSelectedBrush;
    static SetPreferredAppModeFn setPreferredAppMode;
    static FlushMenuThemesFn flushMenuThemes;

    static EditorDarkModeChrome() {
        if (Application.isBatchMode) return;
        AssemblyReloadEvents.beforeAssemblyReload += () => Deactivate(false);
        EditorApplication.quitting += () => Deactivate(false);
        // There is no skin-changed event; Sync is a couple of bool compares.
        EditorApplication.update += Sync;
    }

    static bool Enabled {
        get => EditorPrefs.GetBool(EnabledKey, true);
        set => EditorPrefs.SetBool(EnabledKey, value);
    }

    [MenuItem(MenuPath)]
    static void Toggle() {
        Enabled = !Enabled;
        if (Enabled && !EditorGUIUtility.isProSkin)
            Debug.Log("Dark title bar and menus turn on while the editor uses the Dark theme (Preferences > General > Editor Theme).");
        Sync();
    }

    [MenuItem(MenuPath, true)]
    static bool ToggleValidate() {
        Menu.SetChecked(MenuPath, Enabled);
        return true;
    }

    static void Sync() {
        bool want = Enabled && EditorGUIUtility.isProSkin;
        if (want == active) return;
        if (!want) Deactivate(true);
        else if (IsSupported()) Activate();
    }

    static bool IsSupported() {
        if (supported.HasValue) return supported.Value;
        supported = false;
        if (IntPtr.Size != 8) return false;

        // Both copies would subclass the same windows, and the native one can't be turned off.
        if (GetModuleHandleW(NativeModDll) != IntPtr.Zero) {
            if (!SessionState.GetBool(NativeModWarnedKey, false)) {
                SessionState.SetBool(NativeModWarnedKey, true);
                Debug.LogWarning($"The native {NativeModDll} mod is loaded, so ColdSnap's dark title bar can't be toggled. Close Unity, delete {NativeModDll} from the project and reopen it.");
            }
            return false;
        }

        // SetPreferredAppMode (uxtheme #135) first shipped in Windows 10 1903, build 18362.
        var version = new OSVERSIONINFOW { dwOSVersionInfoSize = Marshal.SizeOf<OSVERSIONINFOW>() };
        if (RtlGetVersion(ref version) != 0 || version.dwBuildNumber < 18362) return false;

        IntPtr uxtheme = LoadLibraryExW("uxtheme.dll", IntPtr.Zero, LOAD_LIBRARY_SEARCH_SYSTEM32);
        if (uxtheme == IntPtr.Zero) return false;
        IntPtr setMode = GetProcAddress(uxtheme, (IntPtr)135);
        IntPtr flush = GetProcAddress(uxtheme, (IntPtr)136);
        if (setMode == IntPtr.Zero || flush == IntPtr.Zero) return false;
        setPreferredAppMode = Marshal.GetDelegateForFunctionPointer<SetPreferredAppModeFn>(setMode);
        flushMenuThemes = Marshal.GetDelegateForFunctionPointer<FlushMenuThemesFn>(flush);

        supported = true;
        return true;
    }

    static void Activate() {
        active = true;
        menuBarBrush = CreateSolidBrush(MenuBarColor);
        menuItemBrush = CreateSolidBrush(MenuItemColor);
        menuItemHotBrush = CreateSolidBrush(MenuItemHotColor);
        menuItemSelectedBrush = CreateSolidBrush(MenuItemSelectedColor);

        // Context menus are drawn by uxtheme itself once the process prefers dark.
        setPreferredAppMode(PreferredAppModeForceDark);
        flushMenuThemes();

        // Catch existing windows, then new ones as they are created on this thread.
        EnumThreadWindows(GetCurrentThreadId(), AttachCallback, IntPtr.Zero);
        cbtHook = SetWindowsHookExW(WH_CBT, CbtCallback, IntPtr.Zero, GetCurrentThreadId());
        RefreshFrames(Subclassed);
    }

    /// <param name="restoreLook">
    ///     False across a domain reload: the title bars and context menus stay dark and only the custom menu bar
    ///     drawing pauses until <see cref="Activate" /> runs again.
    /// </param>
    static void Deactivate(bool restoreLook) {
        if (!active) return;
        active = false;

        if (cbtHook != IntPtr.Zero) {
            UnhookWindowsHookEx(cbtHook);
            cbtHook = IntPtr.Zero;
        }

        var windows = new List<IntPtr>(Subclassed);
        Subclassed.Clear();
        foreach (IntPtr hWnd in windows) {
            RemoveWindowSubclass(hWnd, SubclassCallback, SubclassId);
            if (restoreLook) SetDarkTitleBar(hWnd, false);
        }

        if (menuTheme != IntPtr.Zero) {
            CloseThemeData(menuTheme);
            menuTheme = IntPtr.Zero;
        }
        DeleteObject(menuBarBrush);
        DeleteObject(menuItemBrush);
        DeleteObject(menuItemHotBrush);
        DeleteObject(menuItemSelectedBrush);
        menuBarBrush = menuItemBrush = menuItemHotBrush = menuItemSelectedBrush = IntPtr.Zero;

        if (!restoreLook) return;
        setPreferredAppMode(PreferredAppModeDefault);
        flushMenuThemes();
        RefreshFrames(windows);
    }

    static void Attach(IntPtr hWnd, WindowKind kind) {
        if (kind == WindowKind.None || !Subclassed.Add(hWnd)) return;
        SetDarkTitleBar(hWnd, true);
        SetWindowSubclass(hWnd, SubclassCallback, SubclassId, (UIntPtr)(uint)kind);
    }

    // Redraw the non-client area so a title or menu bar change shows without waiting for the next activation.
    static void RefreshFrames(IEnumerable<IntPtr> windows) {
        foreach (IntPtr hWnd in new List<IntPtr>(windows)) {
            WindowKind kind = Classify(hWnd);
            if (!IsWindow(hWnd) || kind != WindowKind.Unity && kind != WindowKind.Dialog) continue;
            DrawMenuBar(hWnd);
            SetWindowPos(hWnd, IntPtr.Zero, 0, 0, 0, 0, SWP_FRAMECHANGED | SWP_NOMOVE | SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE);
        }
    }

    static void SetDarkTitleBar(IntPtr hWnd, bool dark) {
        int value = dark ? 1 : 0;
        // DWMWA_USE_IMMERSIVE_DARK_MODE is 20 from Windows 10 20H1 on, 19 before it.
        if (DwmSetWindowAttribute(hWnd, 20, ref value, sizeof(int)) != 0)
            DwmSetWindowAttribute(hWnd, 19, ref value, sizeof(int));
    }

    static WindowKind Classify(IntPtr hWnd) {
        TextBuffer.Length = 0;
        GetClassNameW(hWnd, TextBuffer, TextBuffer.Capacity);
        switch (TextBuffer.ToString().ToLowerInvariant()) {
            case "unitycontainerwndclass": return WindowKind.Unity;
            case "#32770": return WindowKind.Dialog;
            case "button": return WindowKind.Button;
            case "tooltips_class32": return WindowKind.Tooltip;
            case "combobox": return WindowKind.ComboBox;
            case "syslistview32": return WindowKind.ListView;
            case "systreeview32": return WindowKind.TreeView;
            default: return WindowKind.None;
        }
    }

    static IntPtr CbtProc(int code, IntPtr wParam, IntPtr lParam) {
        try {
            if (code == HCBT_CREATEWND)
                Attach(wParam, Classify(wParam));
            else if (code == HCBT_DESTROYWND && Subclassed.Remove(wParam))
                RemoveWindowSubclass(wParam, SubclassCallback, SubclassId);
        }
        catch (Exception e) {
            Debug.LogException(e);
        }
        return CallNextHookEx(cbtHook, code, wParam, lParam);
    }

    // Runs for every message the subclassed windows get, so it must never throw back into native code.
    static IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam, UIntPtr id, UIntPtr refData) {
        try {
            var kind = (WindowKind)(uint)refData;
            if (HandleMessage(hWnd, kind, msg, wParam, lParam, out IntPtr result)) return result;
        }
        catch (Exception e) {
            Debug.LogException(e);
        }
        return DefSubclassProc(hWnd, msg, wParam, lParam);
    }

    static bool HandleMessage(IntPtr hWnd, WindowKind kind, uint msg, IntPtr wParam, IntPtr lParam, out IntPtr result) {
        result = IntPtr.Zero;
        bool ownsMenuBar = kind == WindowKind.Unity || kind == WindowKind.Dialog;

        switch (msg) {
            case WM_CTLCOLORDLG:
                result = menuBarBrush;
                return true;

            case WM_CTLCOLORLISTBOX:
                if (kind != WindowKind.ComboBox) return false;
                var info = new COMBOBOXINFO { cbSize = Marshal.SizeOf<COMBOBOXINFO>() };
                if (!GetComboBoxInfo(hWnd, ref info) || info.hwndList != lParam) return false;
                SetBkMode(wParam, OPAQUE);
                goto case WM_CTLCOLORSTATIC;

            case WM_CTLCOLOREDIT:
            case WM_CTLCOLORSCROLLBAR:
            case WM_CTLCOLORSTATIC:
                SetTextColor(wParam, TextColor);
                SetBkColor(wParam, MenuBarColor);
                result = menuBarBrush;
                return true;

            case WM_DRAWITEM: {
                var dis = Marshal.PtrToStructure<DRAWITEMSTRUCT>(lParam);
                if (dis.CtlType != ODT_BUTTON) return false;
                if (dis.itemAction != 0) PaintOwnerDrawButton(dis);
                result = (IntPtr)1;
                return true;
            }

            case WM_ERASEBKGND: {
                if (kind == WindowKind.Button && (GetWindowLongPtrW(hWnd, GWL_STYLE).ToInt64() & 0xF) == BS_GROUPBOX) return false;
                GetClientRect(hWnd, out RECT rc);
                FillRect(wParam, ref rc, menuBarBrush);
                result = (IntPtr)1;
                return true;
            }

            case WM_NCACTIVATE:
            case WM_NCPAINT:
                if (!ownsMenuBar) return false;
                result = DefSubclassProc(hWnd, msg, wParam, lParam);
                DrawMenuBarBottomLine(hWnd);
                return true;

            case WM_NCCREATE:
                SetDarkTitleBar(hWnd, true);
                if (kind == WindowKind.Tooltip || kind == WindowKind.ComboBox) {
                    SetWindowTheme(hWnd, "wstr", "wstr");
                }
                else if (kind == WindowKind.Button) {
                    long style = GetWindowLongPtrW(hWnd, GWL_STYLE).ToInt64();
                    switch (style & 0xFFFF) {
                        case BS_CHECKBOX:
                        case BS_AUTOCHECKBOX:
                        case BS_AUTORADIOBUTTON:
                        case BS_GROUPBOX:
                            SetWindowTheme(hWnd, "wstr", "wstr");
                            break;
                        case BS_PUSHBUTTON:
                        case BS_LEFT:
                            SetWindowLongPtrW(hWnd, GWL_STYLE, (IntPtr)(style | BS_OWNERDRAW));
                            break;
                    }
                }
                return false;

            case WM_PAINT:
                switch (kind) {
                    case WindowKind.Tooltip:
                        SendMessageW(hWnd, TTM_SETTIPBKCOLOR, (IntPtr)MenuBarColor, IntPtr.Zero);
                        SendMessageW(hWnd, TTM_SETTIPTEXTCOLOR, (IntPtr)TextColor, IntPtr.Zero);
                        break;
                    case WindowKind.TreeView:
                        SendMessageW(hWnd, TVM_SETBKCOLOR, IntPtr.Zero, (IntPtr)MenuBarColor);
                        SendMessageW(hWnd, TVM_SETTEXTCOLOR, IntPtr.Zero, (IntPtr)TextColor);
                        break;
                    case WindowKind.ListView:
                        SendMessageW(hWnd, LVM_SETBKCOLOR, IntPtr.Zero, (IntPtr)MenuBarColor);
                        SendMessageW(hWnd, LVM_SETTEXTBKCOLOR, IntPtr.Zero, (IntPtr)MenuBarColor);
                        SendMessageW(hWnd, LVM_SETTEXTCOLOR, IntPtr.Zero, (IntPtr)TextColor);
                        break;
                }
                return false;

            case WM_STYLECHANGING:
            case WM_STYLECHANGED:
                // Kept from the original: letting these through brings back the light menu bar.
                if (kind != WindowKind.Unity) return false;
                result = (IntPtr)1;
                return true;

            case WM_THEMECHANGED:
                if (ownsMenuBar && menuTheme != IntPtr.Zero) {
                    CloseThemeData(menuTheme);
                    menuTheme = IntPtr.Zero;
                }
                return false;

            case WM_UAHDRAWMENU: {
                if (!ownsMenuBar) return false;
                var menu = Marshal.PtrToStructure<UAHMENU>(lParam);
                var mbi = new MENUBARINFO { cbSize = Marshal.SizeOf<MENUBARINFO>() };
                GetMenuBarInfo(hWnd, OBJID_MENU, 0, ref mbi);
                GetWindowRect(hWnd, out RECT window);
                RECT bar = Offset(mbi.rcBar, -window.left, -window.top);
                FillRect(menu.hdc, ref bar, menuBarBrush);
                result = (IntPtr)1;
                return true;
            }

            case WM_UAHDRAWMENUITEM: {
                if (!ownsMenuBar) return false;
                var item = Marshal.PtrToStructure<UAHDRAWMENUITEM>(lParam);
                uint state = item.dis.itemState;

                IntPtr background = menuItemBrush;
                if ((state & ODS_HOTLIGHT) != 0) background = menuItemHotBrush;
                if ((state & ODS_SELECTED) != 0) background = menuItemSelectedBrush;
                bool disabled = (state & (ODS_GRAYED | ODS_DISABLED)) != 0;
                uint flags = DT_CENTER | DT_SINGLELINE | DT_VCENTER;
                if ((state & ODS_NOACCEL) != 0) flags |= DT_HIDEPREFIX;

                TextBuffer.Length = 0;
                GetMenuStringW(item.um.hmenu, (uint)item.iPosition, TextBuffer, TextBuffer.Capacity, MF_BYPOSITION);

                if (menuTheme == IntPtr.Zero) menuTheme = OpenThemeData(hWnd, "Menu");
                var opts = new DTTOPTS {
                    dwSize = Marshal.SizeOf<DTTOPTS>(),
                    dwFlags = DTT_TEXTCOLOR,
                    crText = disabled ? TextColorDisabled : TextColor
                };
                RECT rc = item.dis.rcItem;
                FillRect(item.um.hdc, ref rc, background);
                DrawThemeTextEx(menuTheme, item.um.hdc, MENU_BARITEM, MBI_NORMAL, TextBuffer.ToString(), -1, flags, ref rc, ref opts);
                result = (IntPtr)1;
                return true;
            }
        }
        return false;
    }

    // Windows draws a one-pixel light line between the menu bar and the client area; paint over it.
    static void DrawMenuBarBottomLine(IntPtr hWnd) {
        var mbi = new MENUBARINFO { cbSize = Marshal.SizeOf<MENUBARINFO>() };
        if (!GetMenuBarInfo(hWnd, OBJID_MENU, 0, ref mbi)) return;

        GetClientRect(hWnd, out RECT client);
        MapWindowPoints(hWnd, IntPtr.Zero, ref client, 2);
        GetWindowRect(hWnd, out RECT window);
        client = Offset(client, -window.left, -window.top);
        var line = new RECT { left = client.left, right = client.right, top = client.top - 1, bottom = client.top };

        IntPtr hdc = GetWindowDC(hWnd);
        FillRect(hdc, ref line, menuBarBrush);
        ReleaseDC(hWnd, hdc);
    }

    // Native dialog push buttons are switched to owner-draw in WM_NCCREATE and painted here.
    static void PaintOwnerDrawButton(DRAWITEMSTRUCT dis) {
        IntPtr hWnd = dis.hwndItem, hdc = dis.hDC;
        GetClientRect(hWnd, out RECT rc);

        IntPtr pen = CreatePen(PS_SOLID, 1, TextColor);
        IntPtr oldBrush = SelectObject(hdc, menuBarBrush);
        IntPtr oldPen = SelectObject(hdc, pen);
        IntPtr font = SendMessageW(hWnd, WM_GETFONT, IntPtr.Zero, IntPtr.Zero);
        IntPtr oldFont = font != IntPtr.Zero ? SelectObject(hdc, font) : IntPtr.Zero;
        SetBkColor(hdc, MenuBarColor);
        SetTextColor(hdc, TextColor);

        Rectangle(hdc, 0, 0, rc.right, rc.bottom);
        if (GetFocus() == hWnd) {
            var focus = new RECT { left = rc.left + 2, top = rc.top + 2, right = rc.right - 2, bottom = rc.bottom - 2 };
            DrawFocusRect(hdc, ref focus);
        }

        TextBuffer.Length = 0;
        GetWindowTextW(hWnd, TextBuffer, TextBuffer.Capacity);
        DrawTextW(hdc, TextBuffer.ToString(), -1, ref rc, DT_EDITCONTROL | DT_CENTER | DT_VCENTER | DT_SINGLELINE);

        if (oldFont != IntPtr.Zero) SelectObject(hdc, oldFont);
        SelectObject(hdc, oldPen);
        SelectObject(hdc, oldBrush);
        DeleteObject(pen);
    }

    static uint Rgb(byte r, byte g, byte b) => r | (uint)g << 8 | (uint)b << 16;

    static RECT Offset(RECT rc, int dx, int dy) =>
        new RECT { left = rc.left + dx, top = rc.top + dy, right = rc.right + dx, bottom = rc.bottom + dy };

    #region Win32

    enum WindowKind : uint { None, Unity, Dialog, Button, Tooltip, ComboBox, ListView, TreeView }

    const int PreferredAppModeDefault = 0;
    const int PreferredAppModeForceDark = 2;

    const int WH_CBT = 5;
    const int HCBT_CREATEWND = 3;
    const int HCBT_DESTROYWND = 4;
    const uint LOAD_LIBRARY_SEARCH_SYSTEM32 = 0x800;

    const uint WM_PAINT = 0x000F;
    const uint WM_ERASEBKGND = 0x0014;
    const uint WM_DRAWITEM = 0x002B;
    const uint WM_GETFONT = 0x0031;
    const uint WM_STYLECHANGING = 0x007C;
    const uint WM_STYLECHANGED = 0x007D;
    const uint WM_NCCREATE = 0x0081;
    const uint WM_NCPAINT = 0x0085;
    const uint WM_NCACTIVATE = 0x0086;
    const uint WM_UAHDRAWMENU = 0x0091;
    const uint WM_UAHDRAWMENUITEM = 0x0092;
    const uint WM_CTLCOLOREDIT = 0x0133;
    const uint WM_CTLCOLORLISTBOX = 0x0134;
    const uint WM_CTLCOLORDLG = 0x0136;
    const uint WM_CTLCOLORSCROLLBAR = 0x0137;
    const uint WM_CTLCOLORSTATIC = 0x0138;
    const uint WM_THEMECHANGED = 0x031A;
    const uint TTM_SETTIPBKCOLOR = 0x0413;
    const uint TTM_SETTIPTEXTCOLOR = 0x0414;
    const uint LVM_SETBKCOLOR = 0x1001;
    const uint LVM_SETTEXTCOLOR = 0x1024;
    const uint LVM_SETTEXTBKCOLOR = 0x1026;
    const uint TVM_SETBKCOLOR = 0x111D;
    const uint TVM_SETTEXTCOLOR = 0x111E;

    const int GWL_STYLE = -16;
    const long BS_PUSHBUTTON = 0x0;
    const long BS_CHECKBOX = 0x2;
    const long BS_AUTOCHECKBOX = 0x3;
    const long BS_GROUPBOX = 0x7;
    const long BS_AUTORADIOBUTTON = 0x9;
    const long BS_OWNERDRAW = 0xB;
    const long BS_LEFT = 0x100;

    const uint ODT_BUTTON = 4;
    const uint ODS_SELECTED = 0x0001;
    const uint ODS_GRAYED = 0x0002;
    const uint ODS_DISABLED = 0x0004;
    const uint ODS_HOTLIGHT = 0x0040;
    const uint ODS_NOACCEL = 0x0100;

    const uint DT_CENTER = 0x1;
    const uint DT_VCENTER = 0x4;
    const uint DT_SINGLELINE = 0x20;
    const uint DT_EDITCONTROL = 0x2000;
    const uint DT_HIDEPREFIX = 0x100000;
    const uint DTT_TEXTCOLOR = 0x1;
    const int MENU_BARITEM = 8;
    const int MBI_NORMAL = 1;
    const uint MF_BYPOSITION = 0x400;
    const int OBJID_MENU = unchecked((int)0xFFFFFFFD);
    const int OPAQUE = 2;
    const int PS_SOLID = 0;

    const uint SWP_NOSIZE = 0x1;
    const uint SWP_NOMOVE = 0x2;
    const uint SWP_NOZORDER = 0x4;
    const uint SWP_NOACTIVATE = 0x10;
    const uint SWP_FRAMECHANGED = 0x20;

    delegate IntPtr SubclassProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam, UIntPtr id, UIntPtr refData);
    delegate IntPtr HookProc(int code, IntPtr wParam, IntPtr lParam);
    delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] delegate int SetPreferredAppModeFn(int mode);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] delegate void FlushMenuThemesFn();

    [StructLayout(LayoutKind.Sequential)]
    struct RECT { public int left, top, right, bottom; }

    [StructLayout(LayoutKind.Sequential)]
    struct DRAWITEMSTRUCT
    {
        public uint CtlType, CtlID, itemID, itemAction, itemState;
        public IntPtr hwndItem, hDC;
        public RECT rcItem;
        public UIntPtr itemData;
    }

    // Undocumented menu bar drawing structs (see the original mod); only the leading fields are read.
    [StructLayout(LayoutKind.Sequential)]
    struct UAHMENU
    {
        public IntPtr hmenu, hdc;
        public uint dwFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct UAHDRAWMENUITEM
    {
        public DRAWITEMSTRUCT dis;
        public UAHMENU um;
        public int iPosition;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct MENUBARINFO
    {
        public int cbSize;
        public RECT rcBar;
        public IntPtr hMenu, hwndMenu;
        public int flags;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct COMBOBOXINFO
    {
        public int cbSize;
        public RECT rcItem, rcButton;
        public uint stateButton;
        public IntPtr hwndCombo, hwndItem, hwndList;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct DTTOPTS
    {
        public int dwSize;
        public uint dwFlags, crText, crBorder, crShadow;
        public int iTextShadowType, ptShadowOffsetX, ptShadowOffsetY;
        public int iBorderSize, iFontPropId, iColorPropId, iStateId, fApplyOverlay, iGlowSize;
        public IntPtr pfnDrawTextCallback, lParam;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct OSVERSIONINFOW
    {
        public int dwOSVersionInfoSize, dwMajorVersion, dwMinorVersion, dwBuildNumber, dwPlatformId;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string szCSDVersion;
    }

    [DllImport("comctl32.dll")] static extern bool SetWindowSubclass(IntPtr hWnd, SubclassProc proc, UIntPtr id, UIntPtr refData);
    [DllImport("comctl32.dll")] static extern bool RemoveWindowSubclass(IntPtr hWnd, SubclassProc proc, UIntPtr id);
    [DllImport("comctl32.dll")] static extern IntPtr DefSubclassProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")] static extern IntPtr SetWindowsHookExW(int idHook, HookProc proc, IntPtr hMod, uint threadId);
    [DllImport("user32.dll")] static extern bool UnhookWindowsHookEx(IntPtr hook);
    [DllImport("user32.dll")] static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] static extern bool EnumThreadWindows(uint threadId, EnumWindowsProc proc, IntPtr lParam);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassNameW(IntPtr hWnd, StringBuilder name, int max);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowTextW(IntPtr hWnd, StringBuilder text, int max);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetMenuStringW(IntPtr hMenu, uint item, StringBuilder text, int max, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int DrawTextW(IntPtr hdc, string text, int count, ref RECT rc, uint format);
    [DllImport("user32.dll")] static extern IntPtr SendMessageW(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] static extern IntPtr GetWindowLongPtrW(IntPtr hWnd, int index);
    [DllImport("user32.dll")] static extern IntPtr SetWindowLongPtrW(IntPtr hWnd, int index, IntPtr value);
    [DllImport("user32.dll")] static extern bool GetMenuBarInfo(IntPtr hWnd, int idObject, int idItem, ref MENUBARINFO mbi);
    [DllImport("user32.dll")] static extern bool GetComboBoxInfo(IntPtr hWnd, ref COMBOBOXINFO info);
    [DllImport("user32.dll")] static extern bool GetClientRect(IntPtr hWnd, out RECT rc);
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr hWnd, out RECT rc);
    [DllImport("user32.dll")] static extern int MapWindowPoints(IntPtr from, IntPtr to, ref RECT points, int count);
    [DllImport("user32.dll")] static extern IntPtr GetWindowDC(IntPtr hWnd);
    [DllImport("user32.dll")] static extern int ReleaseDC(IntPtr hWnd, IntPtr hdc);
    [DllImport("user32.dll")] static extern int FillRect(IntPtr hdc, ref RECT rc, IntPtr brush);
    [DllImport("user32.dll")] static extern bool DrawFocusRect(IntPtr hdc, ref RECT rc);
    [DllImport("user32.dll")] static extern IntPtr GetFocus();
    [DllImport("user32.dll")] static extern bool IsWindow(IntPtr hWnd);
    [DllImport("user32.dll")] static extern bool DrawMenuBar(IntPtr hWnd);
    [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);

    [DllImport("gdi32.dll")] static extern IntPtr CreateSolidBrush(uint color);
    [DllImport("gdi32.dll")] static extern IntPtr CreatePen(int style, int width, uint color);
    [DllImport("gdi32.dll")] static extern IntPtr SelectObject(IntPtr hdc, IntPtr obj);
    [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr obj);
    [DllImport("gdi32.dll")] static extern uint SetTextColor(IntPtr hdc, uint color);
    [DllImport("gdi32.dll")] static extern uint SetBkColor(IntPtr hdc, uint color);
    [DllImport("gdi32.dll")] static extern int SetBkMode(IntPtr hdc, int mode);
    [DllImport("gdi32.dll")] static extern bool Rectangle(IntPtr hdc, int left, int top, int right, int bottom);

    [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)] static extern IntPtr OpenThemeData(IntPtr hWnd, string classList);
    [DllImport("uxtheme.dll")] static extern int CloseThemeData(IntPtr theme);
    [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)] static extern int SetWindowTheme(IntPtr hWnd, string appName, string idList);
    [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
    static extern int DrawThemeTextEx(IntPtr theme, IntPtr hdc, int partId, int stateId, string text, int count, uint flags, ref RECT rc, ref DTTOPTS opts);

    [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr hWnd, int attribute, ref int value, int size);

    [DllImport("kernel32.dll")] static extern uint GetCurrentThreadId();
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern IntPtr GetModuleHandleW(string name);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern IntPtr LoadLibraryExW(string name, IntPtr file, uint flags);
    [DllImport("kernel32.dll")] static extern IntPtr GetProcAddress(IntPtr module, IntPtr ordinal);
    [DllImport("ntdll.dll")] static extern int RtlGetVersion(ref OSVERSIONINFOW info);

    #endregion
}

#endif
