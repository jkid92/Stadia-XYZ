@echo off
color 0A
echo =========================================
echo    Stadia X - Battery Check
echo =========================================
echo.

set "BATT="
for /f "delims=" %%i in ('wsl bash -c "bluetoothctl info | grep 'Battery' | sed 's/.*(\([0-9]*\)).*/\1/'" 2^>nul') do set "BATT=%%i"

if "%BATT%"=="" (
    echo [ERROR] Could not read battery level.
    echo Ensure Stadia X is running and the controller is connected.
) else (
    echo Controller Battery Level: %BATT%%%
)

echo.
pause