# Build and Release

This project ships as a portable folder plus two native runtime binaries:

* `stadia_receiver.exe` runs on Windows and exposes the virtual Xbox 360 controller through ViGEmBus.
* `stadia_bridge` runs inside Ubuntu/WSL and forwards Stadia Bluetooth input and rumble packets.

The GUI, batch files, configuration file, `VERSION.txt`, runtime binaries, and documentation are packaged into a ZIP and a Windows installer EXE by the release workflow.
The ZIP also includes `Install-StadiaX.bat`, a lightweight portable installer that copies the folder to a stable install path and creates shortcuts.

## GitHub Actions release flow

The workflow in `.github/workflows/release.yml` does three things:

1. Builds `stadia_bridge` on `ubuntu-latest`.
2. Builds `stadia_receiver.exe` on `windows-latest` with MSVC and downloads the native ViGEmClient SDK files needed for linking.
3. Creates `dist/Stadia-X-<version>.zip`, `dist/Stadia-X-<version>-Setup.exe`, and matching SHA256 files.

Every push to `main` creates downloadable workflow artifacts. Pushing a tag like `v0.3.0` also creates a GitHub Release with the ZIP and installer EXE attached.

```powershell
git tag v0.3.0
git push origin v0.3.0
```

You can also run the workflow manually from the GitHub Actions tab and optionally provide a package version.

## Local Windows receiver build

Requirements:

* Visual Studio Build Tools 2022 or Visual Studio 2022 with the C++ desktop workload.
* PowerShell 5.1 or newer.
* Internet access for the ViGEmClient header, `.lib`, and `.dll` downloads.

Open a Developer PowerShell for VS 2022 from the repository root and run:

```powershell
.\build\Build-WindowsReceiver.ps1
```

This writes `stadia_receiver.exe` and `ViGEmClient.dll` to the repository root.

The receiver still requires the ViGEmBus driver to be installed on the user's machine. If the driver is missing, the receiver starts but reports that ViGEmBus initialization failed.

## Local Linux bridge build

From Ubuntu, WSL, or a Linux CI runner:

```bash
sudo apt-get update
sudo apt-get install -y g++
bash build/build-linux-bridge.sh
```

This writes `stadia_bridge` to the repository root.

## Local package build

After both runtime binaries exist in the repository root:

```powershell
.\build\Package-Release.ps1 -Version local-test
```

The ZIP and SHA256 file are written under `dist/`.

To build the installer locally after packaging, install Inno Setup 6 and run:

```powershell
.\build\Build-Installer.ps1 -Version local-test
```

This writes `Stadia-X-local-test-Setup.exe` and a matching SHA256 file under `dist/`.

For a source-only packaging dry run, use:

```powershell
.\build\Package-Release.ps1 -Version dry-run -AllowMissingBinaries
```

That mode is useful to validate the ZIP layout on a machine without MSVC or Linux build tools.
