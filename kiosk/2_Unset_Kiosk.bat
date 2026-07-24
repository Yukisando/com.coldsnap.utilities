@echo off
:: ============================================================
:: WINDOWS KIOSK REMOVAL TOOL
:: Run this as Administrator to fully undo kiosk_setup.bat.
:: ============================================================
cls
echo ====================================================
echo             WINDOWS KIOSK REMOVAL TOOL
echo ====================================================
echo.

:: --- 1. STOP THE WATCHDOG AND GAME ---
echo [1/4] Stopping watchdog and game processes...
taskkill /FI "WINDOWTITLE eq kiosk_watchdog.bat" /IM cmd.exe /F >nul 2>&1
taskkill /IM kiosk.exe /F >nul 2>&1
echo Done.

:: --- 2. REMOVE THE SHELL OVERRIDE (restores explorer.exe) ---
echo [2/4] Removing kiosk Shell override...
reg delete "HKCU\Software\Microsoft\Windows NT\CurrentVersion\Winlogon" /v Shell /f >nul 2>&1
echo Done.

:: --- 3. DISABLE AUTO-LOGON ---
echo [3/4] Disabling Auto-Logon...
reg add "HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon" /v AutoAdminLogon /t REG_SZ /d 0 /f >nul
reg delete "HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon" /v DefaultPassword /f >nul 2>&1
reg add "HKLM\SYSTEM\CurrentControlSet\Control\Lsa" /v LimitBlankPasswordUse /t REG_DWORD /d 1 /f >nul
echo Done.

:: --- 4. RESTORE A NORMAL DESKTOP RIGHT NOW ---
echo [4/4] Launching Explorer...
start "" explorer.exe

echo.
echo ----------------------------------------------------
echo KIOSK MODE REMOVED
echo ----------------------------------------------------
echo Your desktop is back for this session. The Shell and
echo Auto-Logon settings are fully reverted, and will apply
echo cleanly from the next reboot onward.
echo.
echo IMPORTANT: Your account still has a BLANK password
echo (that part of kiosk_setup.bat is not reversed here,
echo since only you know what you want it set to). Set one
echo with:
echo.
echo     net user %USERNAME% *
echo.
echo (running that from an elevated Command Prompt will
echo prompt you to type a new password).
echo ----------------------------------------------------
pause
