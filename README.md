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

---

## 🚀 Installation & First Run

1. Extract the `Stadia X` folder to a permanent location (e.g., your Desktop or `C:\Program Files\Stadia X`). **Do not run it from inside the ZIP file.**
2. Double-click `Start-Stadia.bat`.
3. **The Setup Phase:**
   * The script will automatically install `usbipd` and `Ubuntu` for WSL.
   * **Note:** You will likely be prompted to **Restart your PC** during the first run. Please restart, and then run `Start-Stadia.bat` again.
4. **First Pairing:**
   * Once the script boots Linux, it will look for your controller.
   * Turn on your Stadia Controller, then hold **Stadia + Y** until the light flashes orange to enter pairing mode.
   * It will connect automatically. Next time you play, you just need to turn the controller on!
5. **Game On!** Leave the black console window open while you play. When you are done, simply close the window and `Stop-Stadia` will automatically run to give your Bluetooth back to Windows.

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