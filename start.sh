#!/bin/bash
echo "[Stadia X] Initializing Bluetooth..."
mkdir -p /dev/input/
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

# Check HID subsystem is available
if [ ! -d /sys/bus/hid ]; then
    echo "[WARNING] HID subsystem not found. Your WSL kernel may be missing HID modules."
    echo "[WARNING] Run 'wsl --update' in Windows PowerShell (as Admin) and restart."
fi

# Start dbus only if not already running
if ! pgrep -x dbus-daemon >/dev/null 2>&1; then
    echo "[Stadia X] Starting D-Bus..."
    mkdir -p /run/dbus
    dbus-daemon --system --fork 2>/dev/null || true
    sleep 1
else
    echo "[Stadia X] D-Bus already running, skipping."
fi

# Kill any stale bluetoothd then start fresh
killall bluetoothd 2>/dev/null || true
sleep 1
bluetoothd &
sleep 2

bluetoothctl power on

# Start scan in background
bluetoothctl scan on >/dev/null 2>&1 &
SCAN_PID=$!
sleep 3

echo "[Stadia X] Scanning for Stadia controller..."

STADIA_MAC=$(bluetoothctl devices | grep -i "Stadia" | head -n 1 | awk '{print $2}')

if [ -n "$STADIA_MAC" ]; then
    echo "[Stadia X] Found previously paired controller: $STADIA_MAC"
    bluetoothctl trust "$STADIA_MAC" >/dev/null 2>&1

    if bluetoothctl info "$STADIA_MAC" 2>/dev/null | grep -q "Connected: yes"; then
        echo "[Stadia X] Controller already connected."
    else
        echo "[Stadia X] Connecting to $STADIA_MAC..."
        bluetoothctl connect "$STADIA_MAC" >/dev/null 2>&1
        sleep 3
    fi
else
    echo "[Stadia X] No previously paired Stadia found. Waiting for pairing..."
    echo "[Stadia X] Hold the Stadia button + Y on your controller for pairing mode."
    for i in $(seq 1 30); do
        sleep 2
        STADIA_MAC=$(bluetoothctl devices | grep -i "Stadia" | head -n 1 | awk '{print $2}')
        if [ -n "$STADIA_MAC" ]; then
            echo "[Stadia X] Found controller: $STADIA_MAC"
            bluetoothctl trust "$STADIA_MAC" >/dev/null 2>&1
            bluetoothctl connect "$STADIA_MAC" >/dev/null 2>&1
            sleep 3
            break
        fi
    done
fi

kill $SCAN_PID 2>/dev/null || true
bluetoothctl scan off >/dev/null 2>&1

if [ -z "$STADIA_MAC" ]; then
    echo "[ERROR] No Stadia controller found. Exiting."
    read -p "Press Enter to exit..."
    exit 1
fi

# Wait for input device to appear
echo "[Stadia X] Waiting for input device to appear..."
TIMEOUT=15
while [ $TIMEOUT -gt 0 ]; do
    if ls /dev/input/event* 2>/dev/null | grep -q event; then
        echo "[Stadia X] Input device confirmed: $(ls /dev/input/event* | tr '\n' ' ')"
        break
    fi
    sleep 1
    TIMEOUT=$((TIMEOUT - 1))
done

if [ $TIMEOUT -eq 0 ]; then
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
    echo "[ERROR] Could not detect Windows host IP. Cannot start bridge."
    read -p "Press Enter to exit..."
    exit 1
fi

echo "[Stadia X] Launching native bridge -> $WIN_IP"
cd /opt/stadia-x
chmod +x stadia_bridge
./stadia_bridge "$WIN_IP"

read -p "Press Enter to exit..."