@echo off
set "WorkDir=%~dp0"

echo --- RESTORING WINDOWS TO NORMAL MODE ---

:: 1. RESTORE DEFAULT SHELL
echo Restoring Explorer.exe...
reg delete "HKCU\Software\Microsoft\Windows NT\CurrentVersion\Winlogon" /v Shell /f

:: 2. DISABLE AUTO-LOGON
echo Disabling Auto-Logon...
reg add "HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon" /v AutoAdminLogon /t REG_SZ /d 0 /f

:: 3. KILL KIOSK PROCESSES
echo Closing Kiosk processes...
taskkill /F /IM cmd.exe /FI "WINDOWTITLE eq kiosk_watchdog.bat" 2>nul
taskkill /F /IM "kiosk.exe" 2>nul

:: 4. CLEAN UP WATCHDOG
if exist "%WorkDir%kiosk_watchdog.bat" (
    del "%WorkDir%kiosk_watchdog.bat"
)

:: 5. RESTART EXPLORER
echo Restarting Desktop...
taskkill /f /im explorer.exe 2>nul
start explorer.exe

echo.
echo Done! Computer restored to normal.
pause