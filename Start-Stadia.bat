@echo off
setlocal enabledelayedexpansion
color 0B
echo =========================================
echo    Stadia X - Native Bridge
echo =========================================
echo.
set "SCRIPT_DIR=%~dp0"
set "SCRIPT_DIR=%SCRIPT_DIR:~0,-1%"
set "REQUESTED_BT_BUSID=%~1"
set "REQUESTED_CONTROLLER_MACS=%~2"
set "REQUESTED_WSL_DISTRO=%~3"
if defined STADIA_X_BT_BUSID set "REQUESTED_BT_BUSID=%STADIA_X_BT_BUSID%"
if exist "%SCRIPT_DIR%\selected_controller_macs.txt" set /p REQUESTED_CONTROLLER_MACS=<"%SCRIPT_DIR%\selected_controller_macs.txt"
if defined STADIA_X_CONTROLLER_MACS set "REQUESTED_CONTROLLER_MACS=%STADIA_X_CONTROLLER_MACS%"
if exist "%SCRIPT_DIR%\selected_wsl_distro.txt" set /p REQUESTED_WSL_DISTRO=<"%SCRIPT_DIR%\selected_wsl_distro.txt"
if defined STADIA_X_WSL_DISTRO set "REQUESTED_WSL_DISTRO=%STADIA_X_WSL_DISTRO%"
set "LOG_DIR=%SCRIPT_DIR%\logs"
set "START_LOG=%LOG_DIR%\start.log"
set "STATUS_FILE=%LOG_DIR%\status.log"
set "BT_DIAG_FILE=%LOG_DIR%\bluetooth-diagnostics.txt"
if not exist "%LOG_DIR%" mkdir "%LOG_DIR%" >nul 2>&1
> "%START_LOG%" echo [%DATE% %TIME%] Stadia X startup requested
> "%STATUS_FILE%" echo [%DATE% %TIME%] STATUS:START_REQUESTED^|Start requested
if defined REQUESTED_CONTROLLER_MACS (
    echo(!REQUESTED_CONTROLLER_MACS!| findstr /R /C:"^[0-9A-Fa-f: ,;][0-9A-Fa-f: ,;]*$" >nul
    if errorlevel 1 (
        call :STATUS CONTROLLER_MANUAL_INVALID "Manual controller MAC selection ignored because it contains invalid characters"
        set "REQUESTED_CONTROLLER_MACS="
    )
)
if defined REQUESTED_CONTROLLER_MACS (
    call :STATUS CONTROLLER_MANUAL_CONFIG "Manual controller MAC selection: !REQUESTED_CONTROLLER_MACS!"
)
if defined REQUESTED_WSL_DISTRO (
    echo(!REQUESTED_WSL_DISTRO!| findstr /R /C:"^[A-Za-z0-9_.-][A-Za-z0-9_.-]*$" >nul
    if errorlevel 1 (
        call :STATUS WSL_DISTRO_INVALID "Requested WSL distro ignored because its name is invalid"
        set "REQUESTED_WSL_DISTRO="
    ) else (
        call :STATUS WSL_DISTRO_REQUESTED "Requested WSL distro: !REQUESTED_WSL_DISTRO!"
    )
)

echo [System Check] Verifying environment...
call :STATUS CHECK_START "Checking Windows and runtime requirements"

set "MISSING_RUNTIME=0"
if not exist "%SCRIPT_DIR%\start.sh" (
    echo ERROR: Missing required file: start.sh
    call :STATUS MISSING_RUNTIME "Missing start.sh"
    set "MISSING_RUNTIME=1"
)
if not exist "%SCRIPT_DIR%\Resolve-WslDistro.ps1" (
    echo ERROR: Missing required file: Resolve-WslDistro.ps1
    call :STATUS MISSING_RUNTIME "Missing Resolve-WslDistro.ps1"
    set "MISSING_RUNTIME=1"
)
if not exist "%SCRIPT_DIR%\stadia_bridge" (
    echo ERROR: Missing required file: stadia_bridge
    call :STATUS MISSING_RUNTIME "Missing stadia_bridge"
    set "MISSING_RUNTIME=1"
)
if not exist "%SCRIPT_DIR%\stadia_receiver.exe" (
    echo ERROR: Missing required file: stadia_receiver.exe
    call :STATUS MISSING_RUNTIME "Missing stadia_receiver.exe"
    set "MISSING_RUNTIME=1"
)
if not exist "%SCRIPT_DIR%\ViGEmClient.dll" (
    echo ERROR: Missing required file: ViGEmClient.dll
    call :STATUS MISSING_RUNTIME "Missing ViGEmClient.dll"
    set "MISSING_RUNTIME=1"
)
if "%MISSING_RUNTIME%"=="1" (
    echo.
    echo The runtime files are not present in this folder.
    echo Build or copy the release artifacts before starting Stadia X.
    pause
    exit /b 1
)

usbipd --version >nul 2>&1
if %errorlevel% neq 0 (
    echo [Setup] USBIPD not found. Installing...
    call :STATUS USBIPD_MISSING "usbipd was not found; launching winget install"
    winget install usbipd
    echo.
    echo ==========================================================
    echo SETUP REQUIREMENT: USBIPD has been installed.
    echo Please RESTART YOUR COMPUTER, then run this script again.
    echo ==========================================================
    pause
    exit /b
)

set "WSL_DISTRO="
for /f "usebackq delims=" %%d in (`powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%SCRIPT_DIR%\Resolve-WslDistro.ps1" -RequestedDistro "%REQUESTED_WSL_DISTRO%"`) do (
    if not defined WSL_DISTRO set "WSL_DISTRO=%%d"
)

if not defined WSL_DISTRO (
    echo [Setup] No usable WSL distro found. Installing Ubuntu WSL userland...
    call :STATUS WSL_DISTRO_MISSING "No usable WSL distro found; launching Ubuntu install"
    wsl --install -d Ubuntu
    echo.
    echo ==========================================================
    echo SETUP REQUIREMENT: Ubuntu installed.
    echo Please RESTART YOUR COMPUTER, then run this script again.
    echo ==========================================================
    pause
    exit /b
)

wsl -d "!WSL_DISTRO!" echo ok >nul 2>&1
if %errorlevel% neq 0 (
    echo ERROR: WSL distro "!WSL_DISTRO!" did not start correctly.
    echo Try opening it once from the Start menu, then run Stadia X again.
    call :STATUS WSL_DISTRO_START_FAILED "WSL distro !WSL_DISTRO! did not start correctly"
    pause
    exit /b 1
)

call :STATUS WSL_DISTRO_SELECTED "Using WSL distro !WSL_DISTRO!"
echo Using WSL distro: !WSL_DISTRO!

REM ---- Check if we even need the custom kernel ----
set "NEED_KERNEL=0"
call :STATUS WSL_KERNEL_CHECK "Checking WSL USB/HID kernel support"
wsl -d "!WSL_DISTRO!" -u root bash -lc "modprobe vhci-hcd 2>/dev/null; lsmod | grep -q vhci_hcd" >nul 2>&1
if %errorlevel% neq 0 set "NEED_KERNEL=1"

if "%NEED_KERNEL%"=="1" (
    if not exist "%USERPROFILE%\wsl_kernel" (
        if not exist "%SCRIPT_DIR%\build\wsl_kernel" (
            echo ERROR: WSL USB/HID support needs a custom kernel, but build\wsl_kernel is missing.
            echo Add the kernel release artifact or update WSL with "wsl --update" and try again.
            call :STATUS WSL_KERNEL_MISSING "Custom WSL kernel is required but build\wsl_kernel is missing"
            pause
            exit /b 1
        )
        echo [Setup] Deploying custom WSL kernel for USB/HID support...
        call :STATUS WSL_KERNEL_DEPLOY "Deploying custom WSL kernel"
        copy "%SCRIPT_DIR%\build\wsl_kernel" "%USERPROFILE%\wsl_kernel" >nul
        if exist "%USERPROFILE%\.wslconfig" (
            if not exist "%USERPROFILE%\.wslconfig.stadia-x.bak" (
                copy "%USERPROFILE%\.wslconfig" "%USERPROFILE%\.wslconfig.stadia-x.bak" >nul
                echo [Setup] Existing .wslconfig backed up to .wslconfig.stadia-x.bak
                call :STATUS WSL_CONFIG_BACKUP "Backed up existing .wslconfig"
            )
        )
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
    call :STATUS WSL_KERNEL_OK "WSL kernel already supports USB HID"
)

REM Only shutdown WSL if receiver is running from old session
tasklist /FI "IMAGENAME eq stadia_receiver.exe" 2>NUL | find /I /N "stadia_receiver.exe">NUL
if "%ERRORLEVEL%"=="0" (
    call :STATUS CLEANUP_OLD_SESSION "Stopping existing receiver and WSL session"
    taskkill /F /IM stadia_receiver.exe >nul 2>&1
    wsl --shutdown >nul 2>&1
    timeout /t 2 /nobreak >nul
)

echo [1/4] Starting WSL...
call :STATUS WSL_START "Starting WSL distro !WSL_DISTRO!"
wsl -d "!WSL_DISTRO!" echo "WSL Booted" >nul 2>&1

echo Waiting for WSL network to initialize...
set /a WSL_WAIT_TRIES=30
:WSL_WAIT
wsl -d "!WSL_DISTRO!" bash -lc "ip addr show eth0 2>/dev/null | grep -q 'inet '" >nul 2>&1
if %errorlevel% neq 0 (
    set /a WSL_WAIT_TRIES-=1
    if !WSL_WAIT_TRIES! leq 0 (
        echo ERROR: Timed out waiting for WSL networking.
        echo Try running "wsl --shutdown", then start Stadia X again.
        call :STATUS WSL_NETWORK_TIMEOUT "Timed out waiting for WSL network"
        pause
        exit /b 1
    )
    timeout /t 2 /nobreak >nul
    goto :WSL_WAIT
)
echo WSL network ready.
call :STATUS WSL_NETWORK_READY "WSL network is ready"

echo.
echo[2/4] Attaching Bluetooth Hardware...
set "BT_BUSID="
echo Auto-detecting Bluetooth adapter...
call :STATUS BT_DETECT "Detecting Bluetooth adapter"

if not "%REQUESTED_BT_BUSID%"=="" (
    set "BT_BUSID=%REQUESTED_BT_BUSID%"
    echo Using Bluetooth BUSID from launcher: %BT_BUSID%
    goto :BT_FOUND
)

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
    call :STATUS BT_MISSING "No Bluetooth BUSID was selected or detected"
    pause
    exit /b 1
)

echo(%BT_BUSID%| findstr /R "^[0-9][0-9]*-[0-9][0-9]*$" >nul
if %errorlevel% neq 0 (
    echo ERROR: Invalid Bluetooth BUSID: %BT_BUSID%
    echo Expected format looks like 1-13.
    call :STATUS BT_INVALID "Invalid Bluetooth BUSID: %BT_BUSID%"
    pause
    exit /b 1
)

set "BT_DEVICE_LINE="
for /f "delims=" %%l in ('usbipd list ^| findstr /R /C:"^[ ]*%BT_BUSID%[ ]"') do (
    if not defined BT_DEVICE_LINE set "BT_DEVICE_LINE=%%l"
)
if not defined BT_DEVICE_LINE (
    echo ERROR: BUSID %BT_BUSID% was not found in usbipd list.
    echo Refresh adapters in the GUI or run "usbipd list" and select the correct BUSID.
    call :STATUS BT_NOT_FOUND_IN_USBIPD "Selected BUSID %BT_BUSID% was not found in usbipd list"
    pause
    exit /b 1
)

echo Success: Target Bluetooth adapter found on BUSID %BT_BUSID%
echo Selected USB/IP device:
echo(!BT_DEVICE_LINE!
call :STATUS BT_SELECTED "Using Bluetooth BUSID %BT_BUSID%"
echo %BT_BUSID%> "%SCRIPT_DIR%\bt_busid.txt"

set ATTACH_TRIES=3
:ATTACH_LOOP
call :STATUS BT_ATTACH_START "Attaching Bluetooth adapter to WSL distro !WSL_DISTRO!"
usbipd bind --busid %BT_BUSID% --force >nul 2>&1
usbipd attach --help 2>nul | findstr /i "distribution" >nul 2>&1
if %errorlevel% equ 0 (
    usbipd attach --wsl --busid %BT_BUSID% --distribution "!WSL_DISTRO!"
    if %errorlevel% neq 0 (
        call :STATUS BT_ATTACH_DISTRO_FALLBACK "Attach with explicit distro failed; retrying default WSL attach"
        usbipd attach --wsl --busid %BT_BUSID%
    )
) else (
    call :STATUS BT_ATTACH_DISTRO_UNSUPPORTED "usbipd does not support explicit WSL distribution attach; using default attach"
    usbipd attach --wsl --busid %BT_BUSID%
)
if %errorlevel% equ 0 goto :ATTACH_SUCCESS

set /a ATTACH_TRIES-=1
if %ATTACH_TRIES% gtr 0 (
    echo WARNING: Attach failed. Retrying in 4 seconds...
    call :STATUS BT_ATTACH_RETRY "Attach failed; retrying"
    timeout /t 4 /nobreak >nul
    goto :ATTACH_LOOP
)

echo ERROR: Could not attach Bluetooth to WSL. Check usbipd and firewall.
call :STATUS BT_ATTACH_FAILED "Could not attach Bluetooth adapter to WSL"
echo Common fixes:
echo   1. Run this script as Administrator
echo   2. Check Windows Firewall is not blocking usbipd
echo   3. Try unplugging and re-plugging a USB Bluetooth dongle
pause
exit /b 1

:ATTACH_SUCCESS
call :STATUS BT_ATTACH_OK "Bluetooth adapter attached to WSL"
usbipd list | findstr /R /C:"^[ ]*%BT_BUSID%[ ]" | findstr /i "Attached" >nul 2>&1
if %errorlevel% equ 0 (
    call :STATUS BT_ATTACH_VERIFY_OK "usbipd reports BUSID %BT_BUSID% as attached"
) else (
    call :STATUS BT_ATTACH_VERIFY_WARN "Attach command succeeded, but usbipd list did not report BUSID %BT_BUSID% as Attached"
)
echo Waiting for Bluetooth adapter to settle in WSL...
timeout /t 5 /nobreak >nul

echo.
echo [3/4] Deploying to Linux...
call :STATUS DEPLOY_START "Deploying Linux bridge files"
for /f "delims=" %%i in ('wsl -d "!WSL_DISTRO!" wslpath -u "%SCRIPT_DIR%"') do set WSL_PATH=%%i
for /f "delims=" %%i in ('wsl -d "!WSL_DISTRO!" wslpath -u "%LOG_DIR%"') do set WSL_LOG_DIR=%%i
wsl -d "!WSL_DISTRO!" -u root mkdir -p /opt/stadia-x
wsl -d "!WSL_DISTRO!" -u root cp "%WSL_PATH%/start.sh" /opt/stadia-x/start.sh
wsl -d "!WSL_DISTRO!" -u root cp "%WSL_PATH%/stadia_bridge" /opt/stadia-x/stadia_bridge
wsl -d "!WSL_DISTRO!" -u root sed -i "s/\r//g" /opt/stadia-x/start.sh
wsl -d "!WSL_DISTRO!" -u root chmod +x /opt/stadia-x/start.sh
call :STATUS DEPLOY_OK "Linux bridge files deployed"

echo.
echo [4/4] Starting Services...
call :STATUS LINUX_START "Starting Linux core"

start /MIN "Stadia X - Linux Core" wsl -d "!WSL_DISTRO!" -u root bash -lc "STADIA_X_STATUS_LOG='%WSL_LOG_DIR%/linux-status.log' STADIA_X_LINUX_LOG='%WSL_LOG_DIR%/linux.log' STADIA_X_BT_DIAG_LOG='%WSL_LOG_DIR%/bluetooth-diagnostics.txt' STADIA_X_CONTROLLER_MACS='%REQUESTED_CONTROLLER_MACS%' /opt/stadia-x/start.sh 2>&1 | tee -a '%WSL_LOG_DIR%/linux.log'"

timeout /t 8 /nobreak >nul

REM --- Get the WSL IP address (Linux IP) so the Windows Receiver can send rumble to it ---
wsl -d "!WSL_DISTRO!" bash -lc "hostname -I" > "%TEMP%\wsl_ip.txt"
set "WSL_IP="
for /f "tokens=1" %%a in ('type "%TEMP%\wsl_ip.txt"') do set "WSL_IP=%%a"

if "%WSL_IP%"=="" (
    echo WARNING: Could not detect WSL IP. Rumble may not work.
    set "WSL_IP=127.0.0.1"
    call :STATUS WSL_IP_FALLBACK "Could not detect WSL IP; using 127.0.0.1"
)

echo Detected WSL IP: %WSL_IP%
call :STATUS WSL_IP_READY "Detected WSL IP %WSL_IP%"

echo.
echo =====================================================================
echo   GAME ON %WSL_IP%
echo   Leave this window open while you play.
echo   Close the Receiver window when done.
echo =====================================================================

call :STATUS RECEIVER_START "Starting Windows receiver"
set "PS_CMD=Start-Process -FilePath '%SCRIPT_DIR%\stadia_receiver.exe' -ArgumentList '%WSL_IP%' -WorkingDirectory '%SCRIPT_DIR%' -Wait; Start-Process -FilePath 'cmd.exe' -ArgumentList '/c \"\"%SCRIPT_DIR%\Stop-Stadia.bat\"\"' -WindowStyle Hidden"
powershell -Command "%PS_CMD%"
exit

:STATUS
echo STATUS:%~1^|%~2
>> "%START_LOG%" echo [%DATE% %TIME%] STATUS:%~1^|%~2
>> "%STATUS_FILE%" echo [%DATE% %TIME%] STATUS:%~1^|%~2
exit /b 0
