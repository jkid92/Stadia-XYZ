# Stadia X Windows Native

This package contains the experimental Windows Native edition of Stadia X. It does not use WSL, usbipd, BlueZ, or the Linux bridge.

Stadia X reads Stadia controller HID input directly from Windows, hides the physical controller through HidHide, and exposes a virtual Xbox 360 controller through ViGEmBus. Games therefore receive one clean input stream instead of duplicated presses.

## First Run

1. Install and launch **Stadia X Windows Native**.
2. Pair the Stadia controller in Windows Bluetooth settings.
3. Press **Start**.
4. Approve the Windows administrator request if a driver needs to be installed or configured.
5. Open **Test input** and press controller buttons to verify the virtual pad.

Start checks HidHide and ViGEmBus, installs missing components through `winget` when possible, protects the physical device, creates up to four virtual Xbox 360 slots, and starts forwarding input. No separate configuration utility is required.

If the controller is not visible, Stadia X opens Windows Bluetooth settings automatically. Pair or reconnect it, return to the app, and press **Check** or **Start** again.

## Main Controls

- **Start**: prepares dependencies and starts the complete virtual controller route.
- **Stop and restore**: stops the receiver and restores physical controller input.
- **Check**: refreshes the detected Stadia controller inventory without starting.
- **Test input**: shows live buttons, sticks, triggers, packet rate, and rumble tests.
- **Logs**: displays connection phases, user actions, and application diagnostics.
- **Support**: creates a troubleshooting bundle.

## Included Files

- `StadiaX.exe`: self-contained Windows Native control center and receiver.
- `ViGEmClient.dll`: native ViGEm client library.
- `VERSION.txt`: package version.
- `assets/`: Windows Native icons and controller test image.

## Recovery

Use **Stop and restore** before troubleshooting the physical controller or uninstalling drivers. The startup path also rolls back HidHide automatically when a later phase fails.

Battery reporting and rumble behavior can vary by controller firmware and Bluetooth stack. A real Stadia controller is required to validate those hardware-dependent paths.
