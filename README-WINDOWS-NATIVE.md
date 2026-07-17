# Stadia X Windows Native

This package contains the experimental Windows Native edition of Stadia X. It does not use WSL, usbipd, BlueZ, or the Linux bridge.

Stadia X reads Stadia controller HID input directly from Windows, hides the physical controller through HidHide, and exposes a virtual Xbox 360 controller through ViGEmBus. Games therefore receive one clean input stream instead of duplicated presses.

## First Run

1. Install and launch **Stadia X Windows Native**.
2. Pair the Stadia controller in Windows Bluetooth settings.
3. Press **Start**.
4. Approve the Windows administrator request if a driver needs to be installed or configured.
5. Open **Test input** and press controller buttons to verify the virtual pad.

Start checks HidHide and ViGEmBus, installs the bundled official components when needed, protects the physical device, creates up to four virtual Xbox 360 slots, and starts forwarding input. The pinned SHA-256 hashes and Nefarius Authenticode publisher are verified before installation; `winget` is not required. No separate configuration utility is needed.

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
- `dependencies/`: official HidHide and ViGEmBus setups plus third-party notices.
- `VERSION.txt`: package version.
- `assets/`: Windows Native icons and controller test image.

## Recovery

Use **Stop and restore** before troubleshooting the physical controller or uninstalling drivers. The startup path also rolls back HidHide automatically when a later phase fails.

Battery reporting uses the level exposed by Windows and feeds the P1-P4 dashboard and compact overlay when available. Battery and rumble behavior can vary by controller firmware and Bluetooth stack; a real Stadia controller is required to validate those hardware-dependent paths.
