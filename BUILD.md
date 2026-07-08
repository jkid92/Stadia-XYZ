# Build and Release

This project ships as a portable folder plus a native GUI and two runtime components:

* `StadiaX.exe` runs on Windows, includes the receiver logic, and exposes virtual Xbox 360 controllers through ViGEmBus.
* `ViGEmClient.dll` is the native ViGEm client library loaded by the integrated receiver.
* `stadia_bridge` runs inside the selected WSL distro and forwards Stadia Bluetooth input and rumble packets.

The native C# GUI executable, fallback scripts, configuration file, `VERSION.txt`, runtime binaries, and documentation are packaged into a ZIP and a Windows installer EXE by the release workflow.
The ZIP also includes `Install-StadiaX.bat`, a lightweight portable installer that copies the folder to a stable install path and creates shortcuts.

## Native C# control center

The primary GUI is a C# WinForms control center under `src/StadiaX.ControlCenter`. It publishes as `StadiaX.exe` and is the normal entry point for users. The executable owns the setup/check flow, Bluetooth and WSL selection, Linux/BlueZ device inspection, controller profiles, macro editing, the integrated Windows receiver, controller telemetry, battery warnings, support bundles, Start/Stop orchestration, native self-test output, log viewing, and GitHub Release checks. The batch and PowerShell tools remain in the package as compatibility/debug entry points.

Build it from a source checkout with:

```powershell
.\build\Build-CSharpControlCenter.ps1 -CopyToRoot
.\StadiaX.exe
```

For quick development without publishing:

```powershell
dotnet build .\src\StadiaX.ControlCenter\StadiaX.ControlCenter.csproj
dotnet run --project .\src\StadiaX.ControlCenter\StadiaX.ControlCenter.csproj
```

## GitHub Actions release flow

The workflow in `.github/workflows/release.yml` does three things:

1. Builds `stadia_bridge` on `ubuntu-latest`.
2. Publishes the C# control center as `StadiaX.exe` and downloads `ViGEmClient.dll`.
3. Creates `dist/Stadia-X-<version>.zip`, `dist/Stadia-X-<version>-Setup.exe`, and matching SHA256 files.

Every push to `main` creates downloadable workflow artifacts. Pushing a tag like `v0.4.0` also creates a GitHub Release with the ZIP and installer EXE attached.

```powershell
git tag v0.4.0
git push origin v0.4.0
```

You can also run the workflow manually from the GitHub Actions tab and optionally provide a package version.

## Local Windows runtime setup

Requirements:

* PowerShell 5.1 or newer.
* Internet access for the ViGEmClient DLL download.

Build the C# app and download the native ViGEm client DLL:

```powershell
.\build\Build-CSharpControlCenter.ps1 -CopyToRoot
.\build\Download-ViGEmClient.ps1
```

This writes `StadiaX.exe` and `ViGEmClient.dll` to the repository root.

The integrated receiver still requires the ViGEmBus driver to be installed on the user's machine. If the driver is missing, the receiver starts but reports that ViGEmBus initialization failed.

## Local Linux bridge build

From Ubuntu, WSL, or a Linux CI runner:

```bash
sudo apt-get update
sudo apt-get install -y g++
bash build/build-linux-bridge.sh
```

This writes `stadia_bridge` to the repository root.

## Local package build

After `StadiaX.exe`, `ViGEmClient.dll`, and `stadia_bridge` exist in the repository root:

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

That mode is useful to validate the ZIP layout on a machine without runtime binaries.

## Windows Native experimental package

The Windows Native experiment ships without WSL, usbipd, BlueZ, or `stadia_bridge`. Build the app and installer with:

```powershell
.\build\Build-CSharpControlCenter.ps1 -CopyToRoot
.\build\Download-ViGEmClient.ps1 -OutputDirectory .
.\build\Package-WindowsNative.ps1 -Version v0.5.20.11
.\build\Build-WindowsNativeInstaller.ps1 -Version v0.5.20.11
```

This writes `Stadia-X-Windows-Native-v0.5.20.11.zip` and `Stadia-X-Windows-Native-v0.5.20.11-Setup.exe` under `dist/`.

Pushing to `windows-native-experiment` also builds the Windows Native setup as a GitHub Actions artifact. Pushing a tag like `windows-native-v0.5.20.11` creates a prerelease with the ZIP, setup EXE, and SHA256 files attached.

After extracting a package or installing Stadia X, run:

```powershell
.\Test-StadiaX.ps1
```

The script writes `logs/self-test.txt` and exits non-zero only when required files, runtime binaries, or core dependencies are missing. The same check is also available from the native GUI. For source-only dry runs, use `-AllowMissingBinaries`.
