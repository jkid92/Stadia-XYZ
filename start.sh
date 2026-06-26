#!/bin/bash
STATUS_LOG="${STADIA_X_STATUS_LOG:-/opt/stadia-x/linux-status.log}"
LINUX_LOG="${STADIA_X_LINUX_LOG:-/opt/stadia-x/linux.log}"
BT_DIAG_LOG="${STADIA_X_BT_DIAG_LOG:-/opt/stadia-x/bluetooth-diagnostics.txt}"
MAX_CONTROLLERS="${STADIA_X_MAX_CONTROLLERS:-4}"
case "$MAX_CONTROLLERS" in
    ''|*[!0-9]*) MAX_CONTROLLERS=4 ;;
esac
if [ "$MAX_CONTROLLERS" -lt 1 ]; then MAX_CONTROLLERS=1; fi
if [ "$MAX_CONTROLLERS" -gt 4 ]; then MAX_CONTROLLERS=4; fi
mkdir -p "$(dirname "$STATUS_LOG")" "$(dirname "$LINUX_LOG")" "$(dirname "$BT_DIAG_LOG")" /dev/input/
SCAN_PID=""
RECOVERY_PID=""
BT_AGENT_PID=""
BT_AGENT_YES_PID=""
BT_AGENT_SCRIPT="/tmp/stadia-x-auto-agent.py"

cleanup_processes() {
    if [ -n "${SCAN_PID:-}" ]; then
        kill "$SCAN_PID" 2>/dev/null || true
        SCAN_PID=""
    fi
    if [ -n "${RECOVERY_PID:-}" ]; then
        kill "$RECOVERY_PID" 2>/dev/null || true
        RECOVERY_PID=""
    fi
    if [ -n "${BT_AGENT_YES_PID:-}" ]; then
        kill "$BT_AGENT_YES_PID" 2>/dev/null || true
        BT_AGENT_YES_PID=""
    fi
    if [ -n "${BT_AGENT_PID:-}" ]; then
        kill "$BT_AGENT_PID" 2>/dev/null || true
        BT_AGENT_PID=""
    fi
    bluetoothctl scan off >/dev/null 2>&1 || true
}

trap cleanup_processes EXIT
trap 'cleanup_processes; exit 130' INT TERM

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

unique_macs() {
    awk 'NF && !seen[toupper($0)]++ { print toupper($0) }'
}

btmgmt_quiet() {
    timeout 4 btmgmt "$@" >/dev/null 2>&1 || true
}

start_bluetooth_agent() {
    if [ -n "${BT_AGENT_PID:-}" ] && kill -0 "$BT_AGENT_PID" 2>/dev/null; then
        return 0
    fi

    cat > "$BT_AGENT_SCRIPT" <<'PY'
#!/usr/bin/env python3
import dbus
import dbus.service
import dbus.mainloop.glib
from gi.repository import GLib

BUS_NAME = "org.bluez"
AGENT_MANAGER_IFACE = "org.bluez.AgentManager1"
AGENT_IFACE = "org.bluez.Agent1"
AGENT_PATH = "/stadiax/agent"

class Agent(dbus.service.Object):
    @dbus.service.method(AGENT_IFACE, in_signature="", out_signature="")
    def Release(self):
        pass

    @dbus.service.method(AGENT_IFACE, in_signature="o", out_signature="s")
    def RequestPinCode(self, device):
        return "0000"

    @dbus.service.method(AGENT_IFACE, in_signature="os", out_signature="")
    def DisplayPinCode(self, device, pincode):
        pass

    @dbus.service.method(AGENT_IFACE, in_signature="o", out_signature="u")
    def RequestPasskey(self, device):
        return dbus.UInt32(0)

    @dbus.service.method(AGENT_IFACE, in_signature="ouq", out_signature="")
    def DisplayPasskey(self, device, passkey, entered):
        pass

    @dbus.service.method(AGENT_IFACE, in_signature="ou", out_signature="")
    def RequestConfirmation(self, device, passkey):
        pass

    @dbus.service.method(AGENT_IFACE, in_signature="o", out_signature="")
    def RequestAuthorization(self, device):
        pass

    @dbus.service.method(AGENT_IFACE, in_signature="os", out_signature="")
    def AuthorizeService(self, device, uuid):
        pass

    @dbus.service.method(AGENT_IFACE, in_signature="", out_signature="")
    def Cancel(self):
        pass

dbus.mainloop.glib.DBusGMainLoop(set_as_default=True)
bus = dbus.SystemBus()
agent = Agent(bus, AGENT_PATH)
manager = dbus.Interface(bus.get_object(BUS_NAME, "/org/bluez"), AGENT_MANAGER_IFACE)
try:
    manager.RegisterAgent(AGENT_PATH, "NoInputNoOutput")
except dbus.exceptions.DBusException:
    pass
manager.RequestDefaultAgent(AGENT_PATH)
print("Stadia X BlueZ auto-agent ready", flush=True)
GLib.MainLoop().run()
PY
    chmod +x "$BT_AGENT_SCRIPT"
    python3 "$BT_AGENT_SCRIPT" >/tmp/stadia-x-bluetooth-agent.log 2>&1 &
    BT_AGENT_PID=$!
    BT_AGENT_YES_PID=""
    bluetoothctl <<'BTCTL' >/dev/null 2>&1 || true
power on
pairable on
discoverable on
BTCTL
    sleep 1
}

configure_bluetooth_pairing() {
    start_bluetooth_agent
    if command -v btmgmt >/dev/null 2>&1; then
        btmgmt_quiet power on
        btmgmt_quiet le on
        btmgmt_quiet bredr on
        btmgmt_quiet connectable on
        btmgmt_quiet io-cap 3
        btmgmt_quiet bondable on
        btmgmt_quiet pairable on
        btmgmt_quiet discov yes 180
    fi

    bluetoothctl <<'BTCTL' >/dev/null 2>&1 || true
power on
pairable on
discoverable on
BTCTL
}

write_bt_diagnostics() {
    local phase="$1"
    {
        echo "Stadia X Bluetooth diagnostics"
        echo "Phase: $phase"
        echo "Timestamp: $(date '+%Y-%m-%d %H:%M:%S')"
        echo
        echo "== bluetoothctl list =="
        bluetoothctl list 2>&1 || true
        echo
        echo "== bluetoothctl show =="
        bluetoothctl show 2>&1 || true
        echo
        echo "== bluetoothctl devices =="
        bluetoothctl devices 2>&1 || true
        DEVICE_MACS=$(bluetoothctl devices 2>/dev/null | awk '{print $2}' | grep -E '^[0-9A-Fa-f:]{17}$' | head -n 20 || true)
        if [ -n "$DEVICE_MACS" ]; then
            echo
            echo "== bluetoothctl info for known devices =="
            while read -r device_mac; do
                [ -z "$device_mac" ] && continue
                echo "-- $device_mac --"
                bluetoothctl info "$device_mac" 2>&1 || true
            done <<< "$DEVICE_MACS"
        fi
        echo
        echo "== hciconfig -a =="
        if command -v hciconfig >/dev/null 2>&1; then hciconfig -a 2>&1 || true; else echo "hciconfig not installed"; fi
        echo
        echo "== btmgmt info =="
        if command -v btmgmt >/dev/null 2>&1; then timeout 5 btmgmt info 2>&1 || true; else echo "btmgmt not installed"; fi
        echo
        echo "== Stadia X bluetooth agent log =="
        tail -n 80 /tmp/stadia-x-bluetooth-agent.log 2>/dev/null || true
        echo
        echo "== rfkill =="
        if command -v rfkill >/dev/null 2>&1; then rfkill list 2>&1 || true; else echo "rfkill not installed"; fi
        echo
        echo "== lsusb bluetooth hints =="
        if command -v lsusb >/dev/null 2>&1; then lsusb 2>&1 | grep -iE "bluetooth|wireless|intel|realtek|mediatek|qualcomm|broadcom" || true; else echo "lsusb not installed"; fi
        echo
        echo "== kernel modules =="
        lsmod 2>/dev/null | grep -E "btusb|bluetooth|vhci|hid|uhid|joydev" || true
        echo
        echo "== dmesg bluetooth tail =="
        dmesg 2>/dev/null | grep -iE "bluetooth|btusb|hci|usbip|vhci|hid" | tail -n 80 || true
    } > "$BT_DIAG_LOG.tmp"
    mv "$BT_DIAG_LOG.tmp" "$BT_DIAG_LOG"
    status "BT_DIAG_WRITTEN" "Bluetooth diagnostics written to $BT_DIAG_LOG"
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
# Load all needed modules â€” fail silently if already built into kernel
modprobe vhci-hcd    2>/dev/null || true
modprobe uhid        2>/dev/null || true
modprobe joydev      2>/dev/null || true
modprobe hid_generic 2>/dev/null || true
modprobe cmac        2>/dev/null || true
modprobe ecb         2>/dev/null || true
modprobe aes_generic 2>/dev/null || true
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
bluetoothd --experimental &
sleep 2

if bluetoothctl power on >/dev/null 2>&1; then
    status "ADAPTER_POWERED" "Bluetooth adapter powered on"
else
    status "ADAPTER_POWER_FAILED" "Could not power on Bluetooth adapter"
fi

configure_bluetooth_pairing
bluetoothctl scan off >/dev/null 2>&1 || true

if bluetoothctl show 2>/dev/null | grep -q "Pairable: yes"; then
    status "PAIRING_READY" "Bluetooth agent, pairable mode, and discovery mode enabled"
else
    status "PAIRING_READY_WARN" "Requested Bluetooth pairing mode, but BlueZ did not confirm Pairable: yes"
fi

STADIA_MAC=""
write_bt_diagnostics "adapter powered"

# Start scan in background
status "SCAN_START" "Scanning for Stadia controller"
bluetoothctl scan on >/dev/null 2>&1 &
SCAN_PID=$!
sleep 3

log "Scanning for Stadia controller..."

discover_stadia_macs() {
    {
        bluetoothctl devices 2>/dev/null
        bluetoothctl devices Paired 2>/dev/null
        bluetoothctl devices Trusted 2>/dev/null
    } | awk 'tolower($0) ~ /stadia/ && $2 ~ /^[0-9A-Fa-f][0-9A-Fa-f](:[0-9A-Fa-f][0-9A-Fa-f]){5}$/ { print $2 }' | unique_macs | head -n "$MAX_CONTROLLERS"
}

scan_for_stadia_macs() {
    local scan_log="/tmp/stadia-x-scan.log"
    timeout 9 bluetoothctl --timeout 7 scan le > "$scan_log" 2>&1 || true
    timeout 9 bluetoothctl --timeout 7 scan on >> "$scan_log" 2>&1 || true
    discover_stadia_macs
    if command -v btmgmt >/dev/null 2>&1; then
        local mgmt_log="/tmp/stadia-x-btmgmt-find.log"
        timeout 9 btmgmt find -l > "$mgmt_log" 2>&1 || true
        awk '
            /dev_found:/ { mac=$2 }
            tolower($0) ~ /stadia/ && mac ~ /^[0-9A-Fa-f][0-9A-Fa-f](:[0-9A-Fa-f][0-9A-Fa-f]){5}$/ { print mac }
        ' "$mgmt_log" | unique_macs
    fi
}

read_manual_controller_macs() {
    local input="${STADIA_X_CONTROLLER_MACS:-}"
    echo "$input" | tr ',;' ' ' | awk '{
        for (i = 1; i <= NF; i++) {
            if ($i ~ /^[0-9A-Fa-f][0-9A-Fa-f](:[0-9A-Fa-f][0-9A-Fa-f]){5}$/) {
                print toupper($i)
            }
        }
    }' | head -n "$MAX_CONTROLLERS"
}

bluetooth_device_known() {
    local mac="$1"
    local info
    info="$(bluetoothctl info "$mac" 2>&1 || true)"
    if printf '%s\n' "$info" | grep -qi "not available"; then
        return 1
    fi
    printf '%s\n' "$info" | grep -Eq "Name:|Alias:|Connected:|UUID:"
}

wait_for_bluetooth_device() {
    local mac="$1"
    local seconds="${2:-60}"
    local elapsed=0

    if bluetooth_device_known "$mac"; then
        return 0
    fi

    status "CONTROLLER_DISCOVER_WAIT" "Controller $mac is not visible to Linux; scanning"
    log "Controller $mac is not visible yet. Put it in pairing mode if its light is not pulsing."

    while [ "$elapsed" -lt "$seconds" ]; do
        configure_bluetooth_pairing
        bluetoothctl scan on >/dev/null 2>&1 || true
        timeout 6 bluetoothctl --timeout 4 scan on >/tmp/stadia-x-manual-scan-${mac//:/}.log 2>&1 || true

        if bluetooth_device_known "$mac"; then
            status "CONTROLLER_DISCOVERED" "Controller $mac is visible to Linux"
            return 0
        fi

        sleep 2
        elapsed=$((elapsed + 8))
        if [ $((elapsed % 16)) -eq 0 ]; then
            status "CONTROLLER_DISCOVER_WAIT" "Still waiting for controller $mac ($elapsed seconds)"
        fi
    done

    status "CONTROLLER_NOT_AVAILABLE" "Controller $mac is not available in BlueZ"
    return 1
}

connect_stadia_controller() {
    local mac="$1"
    local label="$2"

    if [ -z "$STADIA_MAC" ]; then
        STADIA_MAC="$mac"
    fi

    if ! wait_for_bluetooth_device "$mac" 60; then
        return 1
    fi

    status "CONTROLLER_SEEN" "Found $label controller $mac"
    log "Found $label controller: $mac"

    if bluetoothctl info "$mac" 2>/dev/null | grep -q "Connected: yes"; then
        bluetoothctl trust "$mac" >/dev/null 2>&1 || true
        status "CONTROLLER_CONNECTED" "Controller $mac already connected"
        log "Controller $mac already connected."
        return 0
    fi

    status "CONNECT_START" "Connecting to controller $mac"
    log "Connecting to $mac..."
    for attempt in $(seq 1 8); do
        configure_bluetooth_pairing
        bluetoothctl scan on >/dev/null 2>&1 || true
        if [ "$attempt" -eq 4 ]; then
            bluetoothctl remove "$mac" >/dev/null 2>&1 || true
            sleep 1
        fi
        timeout 18 bluetoothctl <<BTCTL >/tmp/stadia-x-pair-${mac//:/}-attempt-${attempt}.log 2>&1 || true
power on
agent NoInputNoOutput
default-agent
pairable on
trust $mac
pair $mac
trust $mac
connect $mac
trust $mac
info $mac
BTCTL
        if command -v btmgmt >/dev/null 2>&1; then
            timeout 20 btmgmt pair -c 3 -t 1 "$mac" >>/tmp/stadia-x-pair-${mac//:/}-attempt-${attempt}.log 2>&1 || true
        fi
        for wait_tick in $(seq 1 5); do
            if bluetoothctl info "$mac" 2>/dev/null | grep -q "Connected: yes"; then
                status "CONNECT_COMMAND_OK" "Connect command completed for $mac on attempt $attempt"
                break 2
            fi
            sleep 2
        done
        status "CONNECT_RETRY" "Controller $mac not connected yet; retry $attempt/8"
    done

    if ! bluetoothctl info "$mac" 2>/dev/null | grep -q "Connected: yes"; then
        status "CONNECT_COMMAND_FAILED" "Connect command failed for $mac"
    fi

    sleep 3
    if bluetoothctl info "$mac" 2>/dev/null | grep -q "Connected: yes"; then
        bluetoothctl trust "$mac" >/dev/null 2>&1 || true
        status "CONTROLLER_CONNECTED" "Controller $mac connected"
        return 0
    fi

    status "CONTROLLER_NOT_CONNECTED" "Controller $mac was seen but did not connect"
    return 1
}

controller_recovery_loop() {
    while true; do
        sleep 10
        for mac in "$@"; do
            [ -z "$mac" ] && continue
            if ! bluetoothctl info "$mac" 2>/dev/null | grep -q "Connected: yes"; then
                status "RECOVERY_RECONNECT" "Controller $mac appears disconnected; reconnecting"
                configure_bluetooth_pairing
                timeout 20 bluetoothctl <<BTCTL >/tmp/stadia-x-recovery-${mac//:/}.log 2>&1 || true
power on
pairable on
trust $mac
connect $mac
trust $mac
info $mac
BTCTL
                if bluetoothctl info "$mac" 2>/dev/null | grep -q "Connected: yes"; then
                    status "RECOVERY_RECONNECT_OK" "Controller $mac reconnected"
                    status "RECOVERY_BRIDGE_RESTART" "Restarting native bridge after Bluetooth reconnect"
                    pkill -TERM -x stadia_bridge >/dev/null 2>&1 || true
                else
                    status "RECOVERY_RECONNECT_FAILED" "Reconnect failed for controller $mac"
                fi
            fi
        done
    done
}

mapfile -t MANUAL_STADIA_MACS < <(read_manual_controller_macs)
write_bt_diagnostics "after initial scan"

CONNECTED_COUNT=0
RECOVERY_MACS=()
if [ ${#MANUAL_STADIA_MACS[@]} -gt 0 ]; then
    status "CONTROLLER_MANUAL_SELECTION" "Using selected controller MAC(s): ${MANUAL_STADIA_MACS[*]}"
    RECOVERY_MACS=("${MANUAL_STADIA_MACS[@]}")
    index=1
    for mac in "${MANUAL_STADIA_MACS[@]}"; do
        if connect_stadia_controller "$mac" "manual #$index"; then
            CONNECTED_COUNT=$((CONNECTED_COUNT + 1))
        fi
        index=$((index + 1))
    done
    if [ "$CONNECTED_COUNT" -eq 0 ]; then
        status "CONTROLLER_MANUAL_FALLBACK" "Selected controller did not connect; falling back to discovery"
    fi
fi

if [ "$CONNECTED_COUNT" -eq 0 ]; then
    mapfile -t STADIA_MACS < <(scan_for_stadia_macs | unique_macs | head -n "$MAX_CONTROLLERS")
    if [ ${#STADIA_MACS[@]} -gt 0 ]; then
        RECOVERY_MACS=("${STADIA_MACS[@]}")
        status "CONTROLLER_SEEN" "Found ${#STADIA_MACS[@]} known Stadia controller(s)"
        index=1
        for mac in "${STADIA_MACS[@]}"; do
            if connect_stadia_controller "$mac" "known #$index"; then
                CONNECTED_COUNT=$((CONNECTED_COUNT + 1))
            fi
            index=$((index + 1))
        done
    else
        status "PAIR_WAIT" "No known Stadia controller found; waiting for pairing"
        log "No previously paired Stadia found. Waiting for pairing..."
        log "Hold Stadia + Y on up to $MAX_CONTROLLERS controllers for pairing mode."
        for i in $(seq 1 30); do
            sleep 2
            if [ $((i % 3)) -eq 1 ]; then
                mapfile -t STADIA_MACS < <(scan_for_stadia_macs | unique_macs | head -n "$MAX_CONTROLLERS")
            else
                mapfile -t STADIA_MACS < <(discover_stadia_macs | unique_macs | head -n "$MAX_CONTROLLERS")
            fi
            if [ ${#STADIA_MACS[@]} -gt 0 ]; then
                RECOVERY_MACS=("${STADIA_MACS[@]}")
                index=1
                for mac in "${STADIA_MACS[@]}"; do
                    if connect_stadia_controller "$mac" "pairing #$index"; then
                        CONNECTED_COUNT=$((CONNECTED_COUNT + 1))
                    fi
                    index=$((index + 1))
                done
                break
            fi
            if [ $((i % 5)) -eq 0 ]; then
                status "PAIR_WAIT" "Still scanning for controller ($((i * 2)) seconds)"
            fi
        done
    fi
fi

if [ -n "$SCAN_PID" ]; then
    kill "$SCAN_PID" 2>/dev/null || true
    SCAN_PID=""
fi
bluetoothctl scan off >/dev/null 2>&1
write_bt_diagnostics "after controller connect attempt"

if [ "$CONNECTED_COUNT" -eq 0 ]; then
    status "CONTROLLER_NOT_FOUND" "No connected Stadia controller found"
    echo "[ERROR] No connected Stadia controller found. Exiting."
    read -p "Press Enter to exit..."
    exit 1
fi

status "CONTROLLERS_READY" "$CONNECTED_COUNT Stadia controller(s) connected"

# Wait for input device to appear
status "INPUT_WAIT" "Waiting for Linux input device"
log "Waiting for input device to appear..."
TIMEOUT=15
while [ $TIMEOUT -gt 0 ]; do
    if ls /dev/input/event* 2>/dev/null | grep -q event; then
        INPUT_EVENTS=$(ls /dev/input/event* | tr '\n' ' ')
        INPUT_COUNT=$(echo "$INPUT_EVENTS" | wc -w)
        status "INPUT_READY" "Input device(s) confirmed ($INPUT_COUNT): $INPUT_EVENTS"
        log "Input device(s) confirmed ($INPUT_COUNT): $INPUT_EVENTS"
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

# Get Windows host IP â€” the default gateway from WSL's perspective
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

RECOVERY_PID=""
if [ ${#RECOVERY_MACS[@]} -gt 0 ]; then
    status "RECOVERY_START" "Bluetooth auto-recovery enabled for ${RECOVERY_MACS[*]}"
    controller_recovery_loop "${RECOVERY_MACS[@]}" &
    RECOVERY_PID=$!
fi

BRIDGE_EXIT=0
BRIDGE_RESTARTS=0
while true; do
    STADIA_X_ENABLE_RUMBLE="${STADIA_X_ENABLE_RUMBLE:-1}" ./stadia_bridge "$WIN_IP"
    BRIDGE_EXIT=$?
    if [ "$BRIDGE_EXIT" -eq 0 ]; then
        break
    fi
    if [ "$BRIDGE_RESTARTS" -ge 3 ]; then
        status "BRIDGE_RESTART_LIMIT" "Native bridge exited repeatedly; not restarting"
        break
    fi
    BRIDGE_RESTARTS=$((BRIDGE_RESTARTS + 1))
    status "BRIDGE_RESTART" "Native bridge exited with code $BRIDGE_EXIT; restarting attempt $BRIDGE_RESTARTS"
    sleep 3
done

if [ -n "$RECOVERY_PID" ]; then
    kill "$RECOVERY_PID" 2>/dev/null || true
    RECOVERY_PID=""
fi
status "BRIDGE_EXIT" "Native bridge exited with code $BRIDGE_EXIT"

read -p "Press Enter to exit..."
