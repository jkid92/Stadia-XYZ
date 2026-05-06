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

REM ---- Check if we need to deploy the custom kernel ----
set "NEED_KERNEL=0"
wsl bash -c "modprobe vhci-hcd 2>/dev/null; lsmod 2>/dev/null | grep -q vhci_hcd" >nul 2>&1
if %errorlevel% neq 0 set "NEED_KERNEL=1"

if "%NEED_KERNEL%"=="1" (
    if not exist "%USERPROFILE%\wsl_kernel" (
        echo [Setup] WSL kernel missing USB/HID support. Deploying custom kernel...
        copy "%SCRIPT_DIR%\build\wsl_kernel" "%USERPROFILE%\wsl_kernel" >nul
        echo [wsl2] > "%USERPROFILE%\.wslconfig"
        echo kernel=C:\\Users\\%USERNAME%\\wsl_kernel >> "%USERPROFILE%\.wslconfig"
        echo memory=800MB >> "%USERPROFILE%\.wslconfig"
        echo processors=2 >> "%USERPROFILE%\.wslconfig"
        echo swap=800MB >> "%USERPROFILE%\.wslconfig"
        echo [Setup] Custom kernel deployed. Restarting WSL...
        wsl --shutdown
        timeout /t 3 /nobreak >nul
    )
) else (
    echo [Setup] WSL kernel already supports USB/HID. Skipping custom kernel.
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

REM ---- Only shut down WSL if a leftover session is running ----
tasklist /FI "IMAGENAME eq stadia_receiver.exe" 2>nul | find /i "stadia_receiver.exe" >nul
if %errorlevel% equ 0 (
    echo [Cleanup] Killing leftover session...
    taskkill /F /IM stadia_receiver.exe >nul 2>&1
    wsl --shutdown >nul 2>&1
    timeout /t 3 /nobreak >nul
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
echo [2/4] Attaching Bluetooth Hardware...

set "BT_BUSID="
echo Auto-detecting Bluetooth adapter...

for /f "tokens=1" %%a in ('usbipd list ^| findstr /i /c:"bluetooth" /c:"intel wireless" /c:"intel(r) wireless" /c:"realtek" /c:"mediatek" /c:"qualcomm"') do (
    set "BT_BUSID=%%a"
    goto :BT_FOUND
)

:BT_FOUND
if "%BT_BUSID%"=="" (
    echo WARNING: Could not auto-detect Bluetooth adapter by name.
    echo Available devices:
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

usbipd bind --busid %BT_BUSID% --force >nul 2>&1

REM Attach with up to 3 retries
set ATTACH_OK=0
for /l %%r in (1,1,3) do (
    if "!ATTACH_OK!"=="0" (
        echo Attach attempt %%r of 3...
        usbipd attach --wsl --busid %BT_BUSID%
        if !errorlevel! equ 0 (
            set ATTACH_OK=1
        ) else (
            echo Retrying in 4 seconds...
            timeout /t 4 /nobreak >nul
        )
    )
)

if "%ATTACH_OK%"=="0" (
    echo ERROR: Could not attach Bluetooth to WSL after 3 attempts.
    echo.
    echo Common fixes:
    echo   1. Run this script as Administrator
    echo   2. Open Device Manager, find "USBIP Shared Device" under
    echo      Universal Serial Bus Controllers, right-click and Uninstall,
    echo      then run this script again.
    pause
    exit /b 1
)

REM Wait for adapter to settle before bluetoothd starts
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

REM Get WSL IP via temp file
timeout /t 8 /nobreak >nul
set "IPFILE=%TEMP%\wsl_ip.txt"
wsl bash -c "ip route show default | grep -oP 'via \K[\d.]+'" > "%IPFILE%" 2>nul
set /p WSL_IP=<"%IPFILE%"
del "%IPFILE%" >nul 2>&1

REM Validate IP
echo %WSL_IP% | findstr /r "^[0-9][0-9]*\.[0-9][0-9]*\.[0-9][0-9]*\.[0-9][0-9]*$" >nul 2>&1
if %errorlevel% neq 0 (
    echo WARNING: IP detection returned "%WSL_IP%", using known fallback.
    set WSL_IP=172.25.112.1
)
echo Detected WSL IP: %WSL_IP%

echo.
echo =====================================================================
echo   GAME ON!  WSL IP: %WSL_IP%
echo   Close the "Stadia Receiver" window when done playing.
echo =====================================================================
echo.

REM Use PowerShell to wait for the receiver and then call Stop-Stadia.
REM This avoids the "Terminate batch job (Y/N)?" prompt that appears
REM when using start /WAIT and the user closes the child window.
powershell -NoProfile -WindowStyle Hidden -Command ^
    "Start-Process -FilePath '%SCRIPT_DIR%\stadia_receiver.exe' -ArgumentList '%WSL_IP%' -WorkingDirectory '%SCRIPT_DIR%' -Wait" 

REM Receiver has exited — run teardown quietly
call "%SCRIPT_DIR%\Stop-Stadia.bat"

exit