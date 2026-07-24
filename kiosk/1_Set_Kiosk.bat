@echo off
:: ============================================================
:: WINDOWS KIOSK SETUP TOOL
:: Run this as Administrator.
:: ============================================================
pushd "%~dp0"
cd /d "%~dp0"

set "WorkDir=%~dp0"
set "GamePath=%WorkDir%kiosk.exe"
set "GameName=kiosk"
set "UserName=%USERNAME%"

cls
echo ====================================================
echo             WINDOWS KIOSK SETUP TOOL
echo ====================================================
echo Current Folder: %WorkDir%
echo Kiosk Account:  %UserName%
echo.

:: --- 1. CREATE THE WATCHDOG ---
echo [1/4] Creating watchdog script...
echo @echo off > "kiosk_watchdog.bat"
echo title kiosk_watchdog.bat >> "kiosk_watchdog.bat"
echo :loop >> "kiosk_watchdog.bat"
echo tasklist ^| findstr /i "%GameName%.exe" ^>nul >> "kiosk_watchdog.bat"
echo if %%errorlevel%% neq 0 ( >> "kiosk_watchdog.bat"
echo     start "" "%GamePath%" >> "kiosk_watchdog.bat"
echo ) >> "kiosk_watchdog.bat"
echo timeout /t 5 ^>nul >> "kiosk_watchdog.bat"
echo goto loop >> "kiosk_watchdog.bat"

if exist "kiosk_watchdog.bat" (
    echo SUCCESS: Watchdog created.
) else (
    echo ERROR: Could not create watchdog file! Check if the folder is Read-Only.
    pause
    exit /b 1
)

:: --- 2. CONFIGURE AUTO-LOGON ---
echo [2/4] Configuring Auto-Logon...
reg add "HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon" /v AutoAdminLogon /t REG_SZ /d 1 /f >nul
reg add "HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon" /v DefaultUserName /t REG_SZ /d "%UserName%" /f >nul
reg add "HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon" /v DefaultPassword /t REG_SZ /d "" /f >nul
reg add "HKLM\SYSTEM\CurrentControlSet\Control\Lsa" /v LimitBlankPasswordUse /t REG_DWORD /d 0 /f >nul

:: --- 3. SET THE KIOSK SHELL ---
echo [3/4] Setting Kiosk Shell override...
reg add "HKCU\Software\Microsoft\Windows NT\CurrentVersion\Winlogon" /v Shell /t REG_SZ /d "cmd.exe /c start /min \"\" \"%WorkDir%kiosk_watchdog.bat\"" /f >nul

:: --- 4. OPTIMIZE POWER ---
echo [4/4] Disabling Sleep and Monitor Timeout...
powercfg /x -hibernate-timeout-ac 0 >nul
powercfg /x -standby-timeout-ac 0 >nul
powercfg /x -monitor-timeout-ac 0 >nul

echo.
echo ----------------------------------------------------
echo SETUP COMPLETE!
echo ----------------------------------------------------
echo On next boot, this machine will auto-logon as "%UserName%"
echo and launch kiosk.exe via the watchdog script.
echo.
echo To exit kiosk mode later, run kiosk_remove.bat as Administrator.
echo ----------------------------------------------------
echo.
set /p DoRestart="Restart now to enter kiosk mode? (Y/N): "
if /i "%DoRestart%"=="Y" (
    echo Restarting in 5 seconds...
    shutdown /r /t 5
) else (
    echo Setup complete. Restart manually whenever you're ready.
    pause
)
