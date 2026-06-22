@echo off
setlocal
set "SCRIPT_DIR=%~dp0"
set "APP=%SCRIPT_DIR%StadiaX.exe"

if not exist "%APP%" (
    echo ERROR: StadiaX.exe was not found next to Stop-Stadia.bat.
    echo Build or install Stadia X first.
    exit /b 1
)

echo Stopping Stadia X bridge and restoring Bluetooth...
powershell.exe -NoProfile -ExecutionPolicy Bypass -Command "Start-Process -FilePath '%APP%' -ArgumentList '--stop-bridge' -WorkingDirectory '%SCRIPT_DIR%' -Verb RunAs -WindowStyle Hidden"
exit /b %ERRORLEVEL%
