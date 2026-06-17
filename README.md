# 🎮 Stadia X - Native Bridge

Stadia X is a low-latency, native bridge that allows you to use your Google Stadia controller via Bluetooth on Windows, complete with **Rumble Support** and a massive **36-key Macro Shortcut System**.

Because Windows natively struggles with the Stadia controller's Bluetooth implementation, this tool seamlessly passes your Bluetooth adapter into a lightweight, custom-built Linux subsystem (WSL2), connects to the controller instantly, and bridges the inputs back to Windows as a flawless Xbox 360 controller.

## ✨ Features
* **Automated Setup:** The script installs everything it needs on first run.
* **Full Rumble Support:** Force feedback works flawlessly.
* **Universal Game Compatibility:** Emulates a standard Xbox 360 controller via ViGEmBus.
* **Ultimate Macro Pad:** Hold the Assistant or Capture buttons to turn the rest of your controller into a media remote or keyboard shortcut machine!
* **Alternate Layouts:** Three ready-made shortcut profiles included — PC, Gaming, and Utils. Just swap the `.ini` file to change your whole layout instantly.
* **Battery Check:** Run `Check-Battery.bat` at any time to see your controller's current battery level.
* **Auto-Restore:** Automatically returns your Bluetooth adapter to Windows when you close the app.

---

## 🛠️ Requirements
1. **Windows 10 or Windows 11**
2. **Hardware Virtualization Enabled:** Ensure VT-x (Intel) or SVM (AMD) is enabled in your motherboard's BIOS (required for WSL2).
3. **Bluetooth Adapter:** Either a built-in motherboard Wi-Fi/BT card or a USB Bluetooth dongle.
4. **ViGEmBus Driver:** Required for Xbox 360 controller emulation on Windows.

---

## 🚀 Installation & First Run

1. Download the latest `Stadia-X-<version>-Setup.exe` from GitHub Releases and run it. The installer copies Stadia X to your user profile and creates shortcuts.
2. If you prefer portable mode, download the release ZIP, extract it, then run `Install-StadiaX.bat` or `Start-GUI.bat`. **Do not run Stadia X from inside the ZIP file.**
3. In the GUI, open `First Run` and follow the checklist from top to bottom.
4. **The Setup Phase:**
   * The script will automatically install `usbipd` and `Ubuntu` for WSL.
   * **Note:** You will likely be prompted to **Restart your PC** during the first run. Please restart, and then run `Start-Stadia.bat` again.
5. **First Pairing:**
   * Once the script boots Linux, it will look for your controller.
   * Turn on your Stadia Controller, then hold **Stadia + Y** until the light flashes orange to enter pairing mode.
   * It will connect automatically. Next time you play, you just need to turn the controller on!
6. **Game On!** Leave the black console window open while you play. When you are done, simply close the window and `Stop-Stadia` will automatically run to give your Bluetooth back to Windows.

---

## 🖥️ Graphical Control Panel

Run `Start-GUI.bat` to open the Stadia X Control Center.

The GUI lets you:
* Follow a first-run checklist that walks through release files, ViGEmBus, usbipd, WSL, Bluetooth selection, startup, and controller testing.
* Check required tools and runtime files before starting.
* Run a pre-start setup audit and a post-start health audit.
* Inspect all USB/IP devices with BUSID, VID:PID, name, state, and Bluetooth detection hint.
* Manually choose or type the exact Bluetooth USB/IP BUSID that should be handed fully to Linux.
* Inspect Windows Bluetooth status, adapters, service state, known devices, and active/OK devices.
* Enable or disable the selected Bluetooth adapter from the GUI when troubleshooting.
* Start the bridge with Administrator elevation when needed.
* Watch live Windows/Linux status events while the bridge starts.
* See whether Linux is scanning, has found the controller, is connecting, or is waiting for an input device.
* Stop Stadia X and restore the Bluetooth adapter.
* Read the controller battery level when the controller is connected.
* Test controller buttons, triggers, and sticks once the updated receiver binary is built.
* Edit and save `stadia_buttons.ini` with automatic timestamped backups.

> Developer note: the source branch must contain or build `stadia_receiver.exe`, `ViGEmClient.dll`, and `stadia_bridge` before the Start button can complete successfully. The GUI reports those missing runtime files clearly instead of failing later in the startup script.

Release packages are built automatically by GitHub Actions. The preferred download is the installer EXE, while the ZIP remains available for portable use and troubleshooting. Both include:
* `Install-StadiaX.bat` and `Install-StadiaX.ps1`
* `stadia_receiver.exe`
* `ViGEmClient.dll`
* `stadia_bridge`

For local builds or release maintenance, see `BUILD.md`.

Runtime logs are written under `logs/`:
* `status.log` records Windows startup and teardown events.
* `linux-status.log` records structured Linux bridge states such as scanning, connecting, and input-device detection.
* `linux.log` keeps the raw Linux core output.
* `bluetooth-diagnostics.txt` captures Linux/BlueZ adapter, controller, module, and kernel hints after the Linux core starts.
* `controller-state.json` is written by the updated Windows receiver and powers the Controller Test screen.

If more than one Bluetooth-looking adapter appears, open the `Setup` tab, select the row that matches your real Bluetooth controller or dongle, and confirm that the same BUSID appears in the `Control` tab before pressing Start. `Start-Stadia.bat` verifies that the chosen BUSID still appears in `usbipd list` and logs whether usbipd reports it as attached after handoff.

The `Bluetooth` tab focuses on the Windows side before USB/IP handoff. It shows whether Windows sees a Bluetooth adapter, whether the Bluetooth service is running, how many non-adapter Bluetooth devices Windows currently reports as active/OK, and all known Bluetooth PnP devices. Windows does not reliably expose the Bluetooth radio specification version or a maximum device count through standard local APIs, so the GUI reports driver information and explains that the practical connection limit depends on adapter, driver, and profile mix.

---

## ⌨️ The Macro Shortcut System

Stadia X unlocks the two middle buttons (**Assistant** and **Capture**) to act like "Shift" keys for your controller, giving you **36 bindable shortcuts** in total (17 Assistant chords + 17 Capture chords + 2 solo presses).

Open `stadia_buttons.ini` in Notepad to configure your shortcuts. By holding Assistant or Capture, you can press any other button on the controller to trigger Windows keyboard shortcuts, media controls, or volume!

**Examples included by default:**
* `Capture + D-Pad Up/Down` = Volume Up/Volume Down
* `Capture + D-Pad Left/Right` = Next/Previous Track
* `Assistant + L3` = Ctrl+Alt+Delete

### 🗂️ Alternate Layouts

Don't want to build your own config from scratch? The `Alternate_Layouts` folder includes three ready-made profiles:

| File | Best for |
|---|---|
| `stadia_buttons PC.ini` | Windows productivity — Copilot, Snipping Tool, window snapping, clipboard |
| `stadia_buttons GAMING.ini` | RPGs & survival games — Inventory, Map, Quick Save, Hotbars, Push-to-Talk |
| `stadia_buttons UTILS.ini` | Capture & streaming — NVIDIA Overlay, Game Bar, screenshots, FPS counter |

To use one, copy it from `Alternate_Layouts` into the main folder and rename it to `stadia_buttons.ini`.

---

## ⚠️ Troubleshooting

**1. Windows Defender / SmartScreen blocks the script or `.exe`**
Because Stadia X uses a custom-compiled executable (`stadia_receiver.exe`) to inject controller and keyboard inputs, some antivirus software may flag it as suspicious. This is a false positive. Click "More Info" → "Run Anyway", or for a permanent fix, add the Stadia X folder to your antivirus exclusions.

**2. Script crashes with "Virtual Machine Platform is not enabled"**
You need to enable hardware virtualization in your BIOS. Look for `VT-x` (Intel) or `SVM / AMD-V` (AMD) and set it to Enabled.

**3. Script asks for my Bluetooth BUSID manually**
Sometimes Windows names your Bluetooth adapter strangely. If it asks for your BUSID, look at the list printed on the screen, find the item that looks like your Bluetooth adapter (e.g., "Intel Wireless Bluetooth"), and type the number next to it (e.g., `1-14`).

**4. `bluetoothctl: command not found`**
This happens on a clean Ubuntu WSL install where the `bluez` package is missing. The script now installs it automatically, but if you hit this on an older version, open a WSL terminal and run:
```
sudo apt-get update && sudo apt-get install -y bluez
```

---

## 🏆 Credits & Acknowledgements
* **[jocxfin]**: Original author of the `stadia-w-rumble-windows` proof-of-concept, which provided the foundational C++ UDP bridge and ViGEm implementation this project was built upon.
* **[Nefarius]**: Creator of the ViGEmBus driver, making Xbox 360 controller emulation possible on Windows.
