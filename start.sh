#!/bin/bash
STATUS_LOG="${STADIA_X_STATUS_LOG:-/opt/stadia-x/linux-status.log}"
LINUX_LOG="${STADIA_X_LINUX_LOG:-/opt/stadia-x/linux.log}"
mkdir -p "$(dirname "$STATUS_LOG")" "$(dirname "$LINUX_LOG")" /dev/input/

status() {
    local code="$1"
    local message="$2"
    local line
    line="STATUS:${code}|${message}"
    echo "$line"
    printf '[%s] %s\n' "$(date '+%Y-%m-%d %H:%M:%S')" "$line" >> "$STATUS_LOG"
}

log() {
    echo "[Stadia X] $1"
}

status "LINUX_INIT" "Initializing Bluetooth services"
log "Initializing Bluetooth..."
mkdir -p /dev/input/

# Ensure bluez is installed (handles fresh Ubuntu WSL installs)
if ! command -v bluetoothctl &>/dev/null; then
    status "BLUEZ_MISSING" "bluetoothctl not found; installing bluez"
    log "bluetoothctl not found. Installing bluez..."
    if apt-get update -qq && apt-get install -y bluez >/dev/null 2>&1; then
        status "BLUEZ_INSTALLED" "bluez installed"
        log "bluez installed."
    else
        status "BLUEZ_INSTALL_FAILED" "bluez installation failed"
        echo "[ERROR] Could not install bluez."
        read -p "Press Enter to exit..."
        exit 1
    fi
else
    status "BLUEZ_OK" "bluetoothctl is available"
fi

# This will load the modules if they are modular,
# and ignore them if they are already built-in.
modprobe joydev 2>/dev/null
modprobe hid-generic 2>/dev/null
modprobe uhid 2>/dev/null
# Load all needed modules — fail silently if already built into kernel
modprobe vhci-hcd    2>/dev/null || true
modprobe uhid        2>/dev/null || true
modprobe joydev      2>/dev/null || true
modprobe hid_generic 2>/dev/null || true
modprobe bluetooth   2>/dev/null || true
modprobe btusb       2>/dev/null || true
status "KERNEL_MODULES_CHECKED" "Bluetooth and HID kernel modules checked"

# Check HID subsystem is available
if [ ! -d /sys/bus/hid ]; then
    status "HID_MISSING" "HID subsystem was not found in WSL"
    echo "[WARNING] HID subsystem not found. Your WSL kernel may be missing HID modules."
    echo "[WARNING] Run 'wsl --update' in Windows PowerShell (as Admin) and restart."
else
    status "HID_OK" "HID subsystem is available"
fi

# Start dbus only if not already running
if ! pgrep -x dbus-daemon >/dev/null 2>&1; then
    status "DBUS_START" "Starting D-Bus"
    log "Starting D-Bus..."
    mkdir -p /run/dbus
    dbus-daemon --system --fork 2>/dev/null || true
    sleep 1
else
    status "DBUS_OK" "D-Bus is already running"
    log "D-Bus already running, skipping."
fi

# Kill any stale bluetoothd then start fresh
status "BLUETOOTHD_START" "Starting bluetoothd"
killall bluetoothd 2>/dev/null || true
sleep 1
bluetoothd &
sleep 2

if bluetoothctl power on >/dev/null 2>&1; then
    status "ADAPTER_POWERED" "Bluetooth adapter powered on"
else
    status "ADAPTER_POWER_FAILED" "Could not power on Bluetooth adapter"
fi

# Start scan in background
status "SCAN_START" "Scanning for Stadia controller"
bluetoothctl scan on >/dev/null 2>&1 &
SCAN_PID=$!
sleep 3

log "Scanning for Stadia controller..."

STADIA_MAC=$(bluetoothctl devices | grep -i "Stadia" | head -n 1 | awk '{print $2}')

if [ -n "$STADIA_MAC" ]; then
    status "CONTROLLER_SEEN" "Found previously paired controller $STADIA_MAC"
    log "Found previously paired controller: $STADIA_MAC"
    bluetoothctl trust "$STADIA_MAC" >/dev/null 2>&1

    if bluetoothctl info "$STADIA_MAC" 2>/dev/null | grep -q "Connected: yes"; then
        status "CONTROLLER_CONNECTED" "Controller already connected"
        log "Controller already connected."
    else
        status "CONNECT_START" "Connecting to controller $STADIA_MAC"
        log "Connecting to $STADIA_MAC..."
        if bluetoothctl connect "$STADIA_MAC" >/dev/null 2>&1; then
            status "CONNECT_COMMAND_OK" "Connect command completed for $STADIA_MAC"
        else
            status "CONNECT_COMMAND_FAILED" "Connect command failed for $STADIA_MAC"
        fi
        sleep 3
        if bluetoothctl info "$STADIA_MAC" 2>/dev/null | grep -q "Connected: yes"; then
            status "CONTROLLER_CONNECTED" "Controller connected"
        else
            status "CONTROLLER_NOT_CONNECTED" "Controller was seen but did not connect"
        fi
    fi
else
    status "PAIR_WAIT" "No known Stadia controller found; waiting for pairing"
    log "No previously paired Stadia found. Waiting for pairing..."
    log "Hold the Stadia button + Y on your controller for pairing mode."
    for i in $(seq 1 30); do
        sleep 2
        STADIA_MAC=$(bluetoothctl devices | grep -i "Stadia" | head -n 1 | awk '{print $2}')
        if [ -n "$STADIA_MAC" ]; then
            status "CONTROLLER_SEEN" "Found controller $STADIA_MAC"
            log "Found controller: $STADIA_MAC"
            bluetoothctl trust "$STADIA_MAC" >/dev/null 2>&1
            status "CONNECT_START" "Connecting to controller $STADIA_MAC"
            if bluetoothctl connect "$STADIA_MAC" >/dev/null 2>&1; then
                status "CONNECT_COMMAND_OK" "Connect command completed for $STADIA_MAC"
            else
                status "CONNECT_COMMAND_FAILED" "Connect command failed for $STADIA_MAC"
            fi
            sleep 3
            if bluetoothctl info "$STADIA_MAC" 2>/dev/null | grep -q "Connected: yes"; then
                status "CONTROLLER_CONNECTED" "Controller connected"
            else
                status "CONTROLLER_NOT_CONNECTED" "Controller was seen but did not connect"
            fi
            break
        fi
        if [ $((i % 5)) -eq 0 ]; then
            status "PAIR_WAIT" "Still scanning for controller ($((i * 2)) seconds)"
        fi
    done
fi

kill $SCAN_PID 2>/dev/null || true
bluetoothctl scan off >/dev/null 2>&1

if [ -z "$STADIA_MAC" ]; then
    status "CONTROLLER_NOT_FOUND" "No Stadia controller found"
    echo "[ERROR] No Stadia controller found. Exiting."
    read -p "Press Enter to exit..."
    exit 1
fi

# Wait for input device to appear
status "INPUT_WAIT" "Waiting for Linux input device"
log "Waiting for input device to appear..."
TIMEOUT=15
while [ $TIMEOUT -gt 0 ]; do
    if ls /dev/input/event* 2>/dev/null | grep -q event; then
        INPUT_EVENTS=$(ls /dev/input/event* | tr '\n' ' ')
        status "INPUT_READY" "Input device confirmed: $INPUT_EVENTS"
        log "Input device confirmed: $INPUT_EVENTS"
        break
    fi
    sleep 1
    TIMEOUT=$((TIMEOUT - 1))
done

if [ $TIMEOUT -eq 0 ]; then
    status "INPUT_TIMEOUT" "No input device appeared after connecting"
    echo "[WARNING] No input device appeared after connecting."
    echo "[WARNING] This usually means missing kernel modules (joydev, hid_generic)."
    echo "[WARNING] Fix: run 'wsl --update' in Windows, restart, and try again."
fi

# Get Windows host IP — the default gateway from WSL's perspective
WIN_IP=$(ip route show default | grep -oP 'via \K[\d.]+')

# Fallback if grep -P unavailable
if [ -z "$WIN_IP" ]; then
    WIN_IP=$(ip route show default | awk '/via/ {print $3}')
fi

if [ -z "$WIN_IP" ]; then
    status "HOST_IP_MISSING" "Could not detect Windows host IP"
    echo "[ERROR] Could not detect Windows host IP. Cannot start bridge."
    read -p "Press Enter to exit..."
    exit 1
fi

status "HOST_IP_READY" "Windows host IP is $WIN_IP"
status "BRIDGE_START" "Launching native bridge"
log "Launching native bridge -> $WIN_IP"
cd /opt/stadia-x
chmod +x stadia_bridge
./stadia_bridge "$WIN_IP"
BRIDGE_EXIT=$?
status "BRIDGE_EXIT" "Native bridge exited with code $BRIDGE_EXIT"

read -p "Press Enter to exit..."
