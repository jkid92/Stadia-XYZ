# Stadia X Release Lines

Stadia X now has one active development line: the unified experimental Windows application built from `windows-native-experiment`.

## Active

- Unified Windows experimental release
- Tag family: `windows-native-v*`
- Current backend: VIIPER 0.7.0 with usbip-win2 0.9.7.8
- One installer identity and one update channel
- HID experiments are included in the regular application

## Historical Snapshots

- Linux/WSL: `v0.5.41`
- Last ViGEm-based Windows release: `windows-native-v0.9.2`

These historical releases remain downloadable for reference but receive no new features or connection fixes. Intermediate beta, native and HID Lab releases can be removed after the next unified Windows release is published.

The independent GPL projects `Scalee/stadia-dongle` and `danzig666/stadia-dongle-esp` are used only as protocol and architecture references. No GPL source code is copied into the MIT-licensed Stadia X codebase.
