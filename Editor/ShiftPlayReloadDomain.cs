#region

using System.Runtime.InteropServices;
using UnityEditor;

#endregion

/// <summary>
///     Enter Play Mode with domain reload disabled by default; hold Shift while pressing Play to reload the domain.
///     The project's Enter Play Mode settings are overridden for the play session only and restored on return to edit mode.
/// </summary>
[InitializeOnLoad]
public static class ShiftPlayReloadDomain
{
    const string OriginalEnabledKey = "ShiftPlayReloadDomain_OriginalEnabled";
    const string OriginalOptionsKey = "ShiftPlayReloadDomain_OriginalOptions";
    const string HasOverrideKey = "ShiftPlayReloadDomain_HasOverride";

    static ShiftPlayReloadDomain() {
        EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        // Recover if the editor was closed or crashed mid-session with the override still applied.
        EditorApplication.delayCall += () => {
            if (!EditorApplication.isPlayingOrWillChangePlaymode) Restore();
        };
    }

    static void OnPlayModeStateChanged(PlayModeStateChange state) {
        switch (state) {
            case PlayModeStateChange.ExitingEditMode:
                Apply(IsShiftHeld());
                break;
            case PlayModeStateChange.EnteredEditMode:
                Restore();
                break;
        }
    }

    static void Apply(bool reloadDomain) {
        if (!SessionState.GetBool(HasOverrideKey, false)) {
            SessionState.SetBool(OriginalEnabledKey, EditorSettings.enterPlayModeOptionsEnabled);
            SessionState.SetInt(OriginalOptionsKey, (int)EditorSettings.enterPlayModeOptions);
            SessionState.SetBool(HasOverrideKey, true);
        }

        if (reloadDomain) {
            // Shift held: full domain + scene reload, as with the option disabled.
            EditorSettings.enterPlayModeOptionsEnabled = false;
        }
        else {
            EditorSettings.enterPlayModeOptionsEnabled = true;
            EditorSettings.enterPlayModeOptions = EnterPlayModeOptions.DisableDomainReload;
        }
    }

    static void Restore() {
        if (!SessionState.GetBool(HasOverrideKey, false)) return;
        EditorSettings.enterPlayModeOptionsEnabled = SessionState.GetBool(OriginalEnabledKey, false);
        EditorSettings.enterPlayModeOptions = (EnterPlayModeOptions)SessionState.GetInt(OriginalOptionsKey, 0);
        SessionState.SetBool(HasOverrideKey, false);
    }

    static bool IsShiftHeld() {
#if UNITY_EDITOR_WIN
        return (GetAsyncKeyState(0x10) & 0x8000) != 0; // VK_SHIFT
#else
        return false;
#endif
    }

#if UNITY_EDITOR_WIN
    [DllImport("user32.dll")]
    static extern short GetAsyncKeyState(int vKey);
#endif
}
