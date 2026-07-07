# Stadia X Windows Native

This package is the experimental Windows Native variant of Stadia X.

It does not use WSL, usbipd, BlueZ, or the Linux bridge. The app reads Stadia Controller HID input directly from Windows, hides the physical controller through HidHide, and exposes a virtual Xbox 360 controller through ViGEmBus.

## Included files

- `StadiaX.exe`: the Windows Native control center and receiver.
- `ViGEmClient.dll`: native ViGEm client library used by the receiver.
- `VERSION.txt`: package version.
- `assets/`: Windows Native app icon and bundled visual assets.

## First run

1. Install and launch `Stadia X Windows Native`.
2. Pair the Stadia Controller in Windows Bluetooth settings.
3. Open the app and press `Probe`.
4. Press `Start native`.
5. Use `Test input` to confirm that only the virtual Xbox 360 controller is sending input.

When HidHide or ViGEmBus is missing, `Start native` tries to install the required component with `winget`. Windows can ask for elevation because both components install drivers.

## Current limitations

- Battery and rumble are still experimental in this variant.
- A real Stadia Controller is required to validate the full input path.
- HidHide is required to prevent duplicated input from the physical controller and the virtual Xbox 360 controller.
