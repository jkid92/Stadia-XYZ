# Stadia X Windows Native

This package contains the experimental Windows Native edition of Stadia X. It does not use WSL, usbipd, BlueZ, or the Linux bridge.

Stadia X reads Stadia controller HID input directly from Windows, hides the physical controller through HidHide, and exposes a virtual Xbox 360 controller through ViGEmBus. Games therefore receive one clean input stream instead of duplicated presses.

The `v0.9.0-beta.3` line separates HID discovery, controller state and mapping from the virtual-gamepad bus. ViGEmBus is still used for Xbox 360 output, but the receiver no longer depends directly on its native API. Legacy WSL bridge commands are disabled in this edition.

## First Run

1. Install and launch **Stadia X Windows Native**.
2. Pair the Stadia controller in Windows Bluetooth settings.
3. Press **Start**.
4. Approve the Windows administrator request if a driver needs to be installed or configured.
5. Open **Mapping + Test** and press controller buttons to verify or customize the virtual pad.

Start checks HidHide and ViGEmBus, installs the bundled official components when needed, protects the physical device, creates up to four virtual Xbox 360 slots, and starts forwarding input. The pinned SHA-256 hashes and Nefarius Authenticode publisher are verified before installation; `winget` is not required. No separate configuration utility is needed.

If the controller is not visible, Stadia X opens Windows Bluetooth settings automatically. Pair or reconnect it, return to the app, and press **Check** or **Start** again.

## Main Controls

- **Start**: prepares dependencies and starts the complete virtual controller route.
- **Stop and restore**: stops the receiver and restores physical controller input.
- **Check**: refreshes the detected Stadia controller inventory without starting.
- **Mapping**: uses an x360ce-style Xbox-first editor. Click the desired output directly on the controller image, then press the physical Stadia button to associate it. The table, selectors, **Record**, and guided **Map all** remain available; use **Save all** to activate the profile while the receiver is running.
- **Map all**: walks through every Xbox button in sequence and records the Stadia input you press for each one.
- **Mapping profiles**: duplicate, rename, activate, or delete independent layouts. Existing schema-1 mapping files are migrated automatically and invalid files never replace the last valid runtime profile.
- **Mapping safety**: assigning an input replaces conflicting assignments, incomplete profiles are highlighted before saving, and unsaved changes are offered for saving when the app closes.
- **Mapping + Test**: combines profiles, button assignments, the live controller image, sticks, triggers, packet rate, and rumble testing. Game rumble from each virtual Xbox pad is routed back to the matching P1-P4 Stadia controller through the native Windows HID output report.
- **Logs**: displays connection phases, user actions, and application diagnostics.
- **Support**: creates a troubleshooting bundle.

## Included Files

- `StadiaX.exe`: self-contained Windows Native control center and receiver.
- `ViGEmClient.dll`: native ViGEm client library.
- `Test-StadiaX.ps1`: package, dependency-hash, driver, and internal runtime verification.
- `dependencies/`: official HidHide and ViGEmBus setups plus third-party notices.
- `VERSION.txt`: package version.
- `assets/`: Windows Native icons and controller test image.

## Recovery

Use **Stop and restore** before troubleshooting the physical controller or uninstalling drivers. The startup path also rolls back HidHide automatically when a later phase fails.

Battery reporting uses the level exposed by Windows and feeds the P1-P4 dashboard and compact overlay when available. The native rumble route uses the same Stadia motor report as the Linux bridge, adapted to Windows HID and dispatched away from the ViGEm callback to avoid feedback stalls. Battery and rumble behavior can vary by controller firmware and Bluetooth stack; a real Stadia controller is required to validate those hardware-dependent paths.
