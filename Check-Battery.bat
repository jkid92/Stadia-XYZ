@echo off
color 0A
echo =========================================
echo    Stadia X - Battery Check
echo =========================================
echo.

set "FOUND="
set "WSL_DISTRO="
for /f "usebackq delims=" %%d in (`powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Resolve-WslDistro.ps1"`) do (
    if not defined WSL_DISTRO set "WSL_DISTRO=%%d"
)

if "%WSL_DISTRO%"=="" (
    echo [ERROR] No usable WSL distro was found.
    echo Start Stadia X once, or install Ubuntu/WSL first.
    echo.
    pause
    exit /b 1
)

echo WSL distro: %WSL_DISTRO%
for /f "tokens=1-4 delims=|" %%a in ('wsl -d "%WSL_DISTRO%" bash -lc "bluetoothctl devices 2>/dev/null | grep -i Stadia | awk '{print $2}' | head -n 4 | while read mac; do [ -z \"$mac\" ] && continue; info=$(bluetoothctl info \"$mac\" 2>/dev/null || true); name=$(printf \"%%s\n\" \"$info\" | awk -F': ' '/Name:/ {print $2; exit}'); connected=$(printf \"%%s\n\" \"$info\" | awk -F': ' '/Connected:/ {print $2; exit}'); batt=$(printf \"%%s\n\" \"$info\" | awk -F'[()]' '/Battery Percentage:/ {print $2; exit}'); printf '%%s|%%s|%%s|%%s\n' \"$mac\" \"${name:-Stadia Controller}\" \"${connected:-unknown}\" \"${batt:-unknown}\"; done" 2^>nul') do (
    set "FOUND=1"
    echo %%b [%%a] - Connected: %%c - Battery: %%d%%
)

if "%FOUND%"=="" (
    echo [ERROR] Could not read any Stadia battery level.
    echo Ensure Stadia X is running and the controller is connected.
)

echo.
pause
