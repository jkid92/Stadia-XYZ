@echo off
setlocal enabledelayedexpansion
color 0B
echo =========================================
echo    Stadia X - Native Bridge
echo =========================================
echo.
set "SCRIPT_DIR=%~dp0"
set "SCRIPT_DIR=%SCRIPT_DIR:~0,-1%"

echo [System Check] Verifying environment...

usbipd --version >nul 2>&1
if %errorlevel% neq 0 (
    echo [Setup] USBIPD not found. Installing...
    winget install usbipd
    echo.
    echo ==========================================================
    echo SETUP REQUIREMENT: USBIPD has been installed.
    echo Please RESTART YOUR COMPUTER, then run this script again.
    echo ==========================================================
    pause
    exit /b
)

REM ---- Check if we even need the custom kernel ----
set "NEED_KERNEL=0"
wsl bash -c "modprobe vhci-hcd 2>/dev/null; lsmod | grep -q vhci_hcd" >nul 2>&1
if %errorlevel% neq 0 set "NEED_KERNEL=1"

if "%NEED_KERNEL%"=="1" (
    if not exist "%USERPROFILE%\wsl_kernel" (
        echo [Setup] Deploying custom WSL kernel for USB/HID support...
        copy "%SCRIPT_DIR%\build\wsl_kernel" "%USERPROFILE%\wsl_kernel" >nul
        echo[wsl2] > "%USERPROFILE%\.wslconfig"
        echo kernel=C:\\Users\\%USERNAME%\\wsl_kernel >> "%USERPROFILE%\.wslconfig"
        echo memory=800MB >> "%USERPROFILE%\.wslconfig"
        echo processors=2 >> "%USERPROFILE%\.wslconfig"
        echo swap=800MB >> "%USERPROFILE%\.wslconfig"
        echo [Setup] Custom kernel deployed. Restarting WSL...
        wsl --shutdown
        timeout /t 3 /nobreak >nul
    )
) else (
    echo [Setup] WSL kernel already supports USB HID. Skipping custom kernel.
)

wsl -d Ubuntu echo ok >nul 2>&1
if %errorlevel% neq 0 (
    echo [Setup] Installing Ubuntu WSL userland...
    wsl --install -d Ubuntu
    echo.
    echo ==========================================================
    echo SETUP REQUIREMENT: Ubuntu installed.
    echo Please RESTART YOUR COMPUTER, then run this script again.
    echo ==========================================================
    pause
    exit /b
)

REM Only shutdown WSL if receiver is running from old session
tasklist /FI "IMAGENAME eq stadia_receiver.exe" 2>NUL | find /I /N "stadia_receiver.exe">NUL
if "%ERRORLEVEL%"=="0" (
    taskkill /F /IM stadia_receiver.exe >nul 2>&1
    wsl --shutdown >nul 2>&1
    timeout /t 2 /nobreak >nul
)

echo [1/4] Starting WSL...
wsl echo "WSL Booted" >nul 2>&1

echo Waiting for WSL network to initialize...
:WSL_WAIT
wsl bash -c "ip addr show eth0 2>/dev/null | grep -q 'inet '" >nul 2>&1
if %errorlevel% neq 0 (
    timeout /t 2 /nobreak >nul
    goto :WSL_WAIT
)
echo WSL network ready.

echo.
echo[2/4] Attaching Bluetooth Hardware...
set "BT_BUSID="
echo Auto-detecting Bluetooth adapter...

for /f "tokens=1" %%a in ('usbipd list ^| findstr /i /c:"bluetooth" /c:"intel wireless" /c:"intel(r) wireless" /c:"realtek" /c:"mediatek" /c:"qualcomm"') do (
    set "BT_BUSID=%%a"
    goto :BT_FOUND
)

:BT_FOUND
if "%BT_BUSID%"=="" (
    echo WARNING: Could not auto-detect Bluetooth adapter by name.
    usbipd list
    echo.
    set /p "BT_BUSID=Enter the BUSID of your Bluetooth adapter (e.g. 1-13): "
)

if "%BT_BUSID%"=="" (
    echo ERROR: No Bluetooth adapter provided. Cannot continue.
    pause
    exit /b 1
)

echo Success: Target Bluetooth adapter found on BUSID %BT_BUSID%
echo %BT_BUSID%> "%SCRIPT_DIR%\bt_busid.txt"

set ATTACH_TRIES=3
:ATTACH_LOOP
usbipd bind --busid %BT_BUSID% --force >nul 2>&1
usbipd attach --wsl --busid %BT_BUSID%
if %errorlevel% equ 0 goto :ATTACH_SUCCESS

set /a ATTACH_TRIES-=1
if %ATTACH_TRIES% gtr 0 (
    echo WARNING: Attach failed. Retrying in 4 seconds...
    timeout /t 4 /nobreak >nul
    goto :ATTACH_LOOP
)

echo ERROR: Could not attach Bluetooth to WSL. Check usbipd and firewall.
echo Common fixes:
echo   1. Run this script as Administrator
echo   2. Check Windows Firewall is not blocking usbipd
echo   3. Try unplugging and re-plugging a USB Bluetooth dongle
pause
exit /b 1

:ATTACH_SUCCESS
echo Waiting for Bluetooth adapter to settle in WSL...
timeout /t 5 /nobreak >nul

echo.
echo [3/4] Deploying to Linux...
for /f "delims=" %%i in ('wsl wslpath -u "%SCRIPT_DIR%"') do set WSL_PATH=%%i
wsl -u root mkdir -p /opt/stadia-x
wsl -u root cp "%WSL_PATH%/start.sh" /opt/stadia-x/start.sh
wsl -u root cp "%WSL_PATH%/stadia_bridge" /opt/stadia-x/stadia_bridge
wsl -u root sed -i "s/\r//g" /opt/stadia-x/start.sh
wsl -u root chmod +x /opt/stadia-x/start.sh

echo.
echo [4/4] Starting Services...

start /MIN "Stadia X - Linux Core" wsl -u root bash -c "/opt/stadia-x/start.sh"

timeout /t 8 /nobreak >nul

REM --- Get the WSL IP address (Linux IP) so the Windows Receiver can send rumble to it ---
wsl bash -c "hostname -I" > "%TEMP%\wsl_ip.txt"
set "WSL_IP="
for /f "tokens=1" %%a in ('type "%TEMP%\wsl_ip.txt"') do set "WSL_IP=%%a"

if "%WSL_IP%"=="" (
    echo WARNING: Could not detect WSL IP. Rumble may not work.
    set "WSL_IP=127.0.0.1"
)

echo Detected WSL IP: %WSL_IP%

echo.
echo =====================================================================
echo   GAME ON %WSL_IP%
echo   Leave this window open while you play.
echo   Close the Receiver window when done.
echo =====================================================================

set "PS_CMD=Start-Process -FilePath '%SCRIPT_DIR%\stadia_receiver.exe' -ArgumentList '%WSL_IP%' -WorkingDirectory '%SCRIPT_DIR%' -Wait; Start-Process -FilePath 'cmd.exe' -ArgumentList '/c \"\"%SCRIPT_DIR%\Stop-Stadia.bat\"\"' -WindowStyle Hidden"
powershell -Command "%PS_CMD%"
exit