@echo off
setlocal enabledelayedexpansion
color 0C
echo =========================================
echo    Stadia X - Teardown
echo =========================================
echo.
set "SCRIPT_DIR=%~dp0"
set "SCRIPT_DIR=%SCRIPT_DIR:~0,-1%"
set "LOG_DIR=%SCRIPT_DIR%\logs"
set "STOP_LOG=%LOG_DIR%\teardown.log"
set "STATUS_FILE=%LOG_DIR%\status.log"
if not exist "%LOG_DIR%" mkdir "%LOG_DIR%" >nul 2>&1
>> "%STOP_LOG%" echo [%DATE% %TIME%] Stadia X teardown requested
call :STATUS STOP_START "Stopping Stadia X and restoring Bluetooth"

echo [1/4] Terminating processes...
taskkill /F /IM stadia_receiver.exe >nul 2>&1
taskkill /F /IM stadia-vigem-x86.exe >nul 2>&1

echo [2/4] Shutting down WSL...
wsl --shutdown >nul 2>&1
timeout /t 2 /nobreak >nul

echo [3/4] Detaching Bluetooth adapter...
set "BT_BUSID="

REM Primary: saved bus ID from Start-Stadia
if exist "%SCRIPT_DIR%\bt_busid.txt" (
    set /p BT_BUSID=<"%SCRIPT_DIR%\bt_busid.txt"
    for /f "tokens=* delims= " %%a in ("!BT_BUSID!") do set BT_BUSID=%%a
    echo    Using saved Bus ID: !BT_BUSID!
)

REM Fallbacks if file missing
if "!BT_BUSID!"=="" (
    for /f "tokens=1 delims= " %%a in ('usbipd list 2^>nul ^| findstr /i "bluetooth"') do (
        if "!BT_BUSID!"=="" set BT_BUSID=%%a
    )
)
if "!BT_BUSID!"=="" (
    for /f "tokens=1 delims= " %%a in ('usbipd list 2^>nul ^| findstr /i "USBIP Shared"') do (
        if "!BT_BUSID!"=="" set BT_BUSID=%%a
    )
)
if "!BT_BUSID!"=="" (
    for /f "tokens=1 delims= " %%a in ('usbipd list 2^>nul ^| findstr /i "intel wireless"') do (
        if "!BT_BUSID!"=="" set BT_BUSID=%%a
    )
)

if not "!BT_BUSID!"=="" (
    echo(!BT_BUSID!| findstr /R /C:"^[0-9][0-9]*-[0-9][0-9]*$" >nul
    if errorlevel 1 (
        echo    WARNING: Ignoring invalid Bluetooth Bus ID: !BT_BUSID!
        call :STATUS BT_RESTORE_INVALID "Invalid Bluetooth BUSID ignored during restore"
        set "BT_BUSID="
    )
)

if "!BT_BUSID!"=="" (
    echo    WARNING: No Bluetooth adapter found to detach.
    call :STATUS BT_RESTORE_UNKNOWN "No Bluetooth BUSID found for detach"
    echo    If Bluetooth is missing from Windows: open Device Manager,
    echo    find "USBIP Shared Device" under Universal Serial Bus Controllers,
    echo    right-click and Uninstall Device, then Action - Scan for hardware changes.
) else (
    echo    Releasing Bus ID: !BT_BUSID!
    call :STATUS BT_RESTORE_START "Releasing Bluetooth BUSID !BT_BUSID!"
    usbipd detach --busid !BT_BUSID! >nul 2>&1
    timeout /t 1 /nobreak >nul
    usbipd unbind  --busid !BT_BUSID! >nul 2>&1

    REM Verify detached
    usbipd list 2>nul | findstr /i "!BT_BUSID!" | findstr /i "Attached" >nul 2>&1
    if !errorlevel! equ 0 (
        echo    Still attached, forcing unbind...
        call :STATUS BT_RESTORE_RETRY "Adapter still attached; forcing unbind"
        usbipd unbind --busid !BT_BUSID! --force >nul 2>&1
    ) else (
        echo    Bluetooth adapter returned to Windows successfully.
        call :STATUS BT_RESTORE_OK "Bluetooth adapter returned to Windows"
    )
    if exist "%SCRIPT_DIR%\bt_busid.txt" del "%SCRIPT_DIR%\bt_busid.txt"
)

echo [4/4] Waiting for Windows to re-enumerate Bluetooth...
timeout /t 3 /nobreak >nul

echo.
echo =========================================
echo   Teardown complete.
echo   Your Bluetooth has been restored.
echo =========================================
echo.
echo   If Bluetooth is still missing: open Device Manager,
echo   Action menu - Scan for hardware changes.
echo.
call :STATUS STOP_DONE "Teardown complete"
timeout /t 5
exit /b 0

:STATUS
echo STATUS:%~1^|%~2
>> "%STOP_LOG%" echo [%DATE% %TIME%] STATUS:%~1^|%~2
>> "%STATUS_FILE%" echo [%DATE% %TIME%] STATUS:%~1^|%~2
exit /b 0
