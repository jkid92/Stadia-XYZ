# Stadia X Windows Native

This package is the single experimental Windows edition of Stadia X. It does not use WSL, usbipd, BlueZ, or the Linux bridge. The former regular Windows Native and HID Lab packages are consolidated here.

Stadia X reads Stadia controller HID input directly from Windows, hides the physical controller through HidHide, and exposes a virtual Xbox 360 controller through VIIPER and usbip-win2. Games therefore receive one clean input stream instead of duplicated presses.

The current line separates BLE discovery, HID input, controller state and mapping from the virtual-gamepad bus. It includes modern Windows Bluetooth LE discovery with a Win32 fallback, direct BLE battery fallback, precise analog-stick visualization, persistent per-controller vibration controls, stable P1-P4 ordering profiles, native Assistant/Capture macros, automatic multi-controller slot expansion, and Windows-only repair and diagnostics. VIIPER runs as a private local process and creates the Xbox 360 devices through the signed usbip-win2 driver. Legacy WSL bridge commands are disabled.

## First Run

1. Run the setup and approve its single Windows administrator request.
2. Restart Windows once if setup has just installed usbip-win2 and asks for it.
3. Launch **Stadia X Windows Native**.
4. Put an unpaired Stadia controller in Bluetooth pairing mode.
5. Press **Start**. Pairing and receiver startup continue automatically.
6. Open **Mapping + Test** and press controller buttons to verify or customize the virtual pad.

Setup installs HidHide and usbip-win2 silently when they are missing. It bundles the self-contained .NET 10 application and VIIPER runtime, so no other runtime or configuration utility is needed. Start verifies every component again and retains an automatic repair fallback, then protects the physical device, creates up to four virtual Xbox 360 slots, and starts forwarding input. Pinned SHA-256 hashes and expected Authenticode publishers are verified while building the package; `winget` is not required.

If the controller is not visible, keep it in Bluetooth pairing mode and press **Start** again. The **Repair** command can restore HidHide, restart known Stadia PnP devices, rescan Windows hardware, and relaunch the receiver automatically.

## Main Controls

- **Start**: prepares dependencies and starts the complete virtual controller route.
- **Stop and restore**: stops the receiver and restores physical controller input.
- **Check**: refreshes the detected Stadia controller inventory without starting.
- **Mapping**: uses an x360ce-style Xbox-first editor. Click the desired output directly on the controller image, then press the physical Stadia button to associate it. The table, selectors, **Record**, and guided **Map all** remain available; use **Save all** to activate the profile while the receiver is running.
- **Map all**: walks through every Xbox button in sequence and records the Stadia input you press for each one.
- **Mapping profiles**: duplicate, rename, activate, or delete independent layouts. Existing schema-1 mapping files are migrated automatically and invalid files never replace the last valid runtime profile.
- **Mapping safety**: assigning an input replaces conflicting assignments, incomplete profiles are highlighted before saving, and unsaved changes are offered for saving when the app closes.
- **Mapping + Test**: combines profiles, button assignments, the live controller image, sticks, triggers, packet rate, and rumble testing. Game rumble from each virtual Xbox pad is routed back to the matching P1-P4 Stadia controller through the native Windows HID output report.
- **HID output modes**: the Home button cycles between `Auto`, the original HidSharp stream, a dedicated Win32 `WriteFile` handle, `HidD_SetOutputReport`, and the experimental `HidD_SetFeature` path. Changes are picked up by the running receiver; press **Vibrate** after each change and use the Windows Native log to compare the requested and effective route.
- **Controller profiles**: save the Bluetooth address and preferred P1-P4 position. Profiles are applied before receiver startup so reconnect order stays predictable.
- **Macros**: Assistant and Capture provide the same 36 configurable shortcut slots as the Linux edition. Macro dispatch runs outside the HID input loop and configuration changes are reloaded while the receiver is active.
- **Controller Doctor**: checks Windows Bluetooth, Stadia HID visibility, HidHide isolation, virtual pads, battery, vibration, profiles, macros, and live input.
- **Repair**: stops the receiver safely, restores physical input, starts the Bluetooth service, restarts known Stadia PnP devices, rescans hardware, and relaunches the receiver.
- **Multi-controller expansion**: while the app is open, newly visible Stadia controllers automatically expand the receiver up to four virtual slots.
- **Logs**: displays connection phases, user actions, and application diagnostics.
- **Support**: creates a troubleshooting bundle.

## Included Files

- `StadiaX.exe`: self-contained Windows Native control center and receiver.
- `Test-StadiaX.ps1`: package, dependency-hash, driver, and internal runtime verification.
- `dependencies/VIIPER/viiper.exe`: bundled official VIIPER 0.7.0 standalone server.
- `dependencies/USBip-0.9.7.8-x64.exe`: bundled official usbip-win2 driver setup.
- `dependencies/`: HidHide setup, VIIPER runtime, usbip-win2 setup, and third-party notices.
- `VERSION.txt`: package version.
- `assets/`: Windows Native icons and controller test image.
- `stadia_buttons.ini`: editable Assistant/Capture shortcut configuration used directly by the Windows Native receiver.

## Recovery

Use **Stop and restore** before troubleshooting the physical controller or uninstalling drivers. The startup path also rolls back HidHide automatically when a later phase fails.

Battery reporting first uses the level exposed by Windows and then tries the standard Stadia BLE Battery Service (`0x180F`, characteristic `0x2A19`). The native rumble route uses output report `5` with two little-endian 16-bit motor values, dispatched away from the VIIPER feedback callback to avoid stalls. Battery and rumble behavior can vary by controller firmware and Bluetooth stack; a real Stadia controller is required to validate those hardware-dependent paths.

`Auto` tries a separate `WriteFile` output handle first, keeps the first working route for subsequent packets, and falls back to the original stream and then `HidD_SetOutputReport`. `HidD_SetFeature` is intentionally manual because many HID devices do not expose the Stadia rumble command as a feature report.

There is no longer a separate HID Lab installer. HID modes, diagnostics, mappings and updates all belong to this package.
