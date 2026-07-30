# Build and Release

Stadia X has one active Windows line. The application reads Stadia HID input directly, creates Xbox 360 devices through VIIPER and usbip-win2, and uses HidHide to prevent duplicate physical input. The historical Linux/WSL and previous Windows generations remain available only in earlier GitHub releases.

## Requirements

- Windows 10 or Windows 11, 64-bit.
- PowerShell 5.1 or newer.
- .NET 10 SDK.
- Inno Setup 6 to build the setup executable.
- Internet access when refreshing pinned third-party dependencies.

The published application is self-contained; end users do not need to install .NET.

## Build The Application

```powershell
dotnet build .\src\StadiaX.ControlCenter\StadiaX.ControlCenter.csproj -c Release
.\build\Build-CSharpControlCenter.ps1 -CopyToRoot
```

The second command publishes the self-contained runtime as `StadiaX.exe` in the repository root.

## Refresh Dependencies

```powershell
.\build\Download-WindowsNativeDependencies.ps1
```

The script downloads the pinned official HidHide, VIIPER, and usbip-win2 releases. It verifies SHA-256 hashes and expected Authenticode publishers before the files can be packaged.

VIIPER is bundled as a standalone server and is started privately by Stadia X. Its C# client is restored from NuGet during the .NET build. Setup runs elevated once, installs HidHide and usbip-win2 silently when missing, and requests a Windows restart when usbip-win2 has just been installed. The application keeps the same checks as a repair fallback.

## Verify

```powershell
.\StadiaX.exe --internal-self-test
.\StadiaX.exe --viiper-smoke-test
.\Test-StadiaX.ps1
.\build\Test-UiLayouts.ps1
```

The smoke test creates a real virtual Xbox 360 device, sends a short test state, neutralizes it, and removes it. The UI audit runs only the supported 1280x820 at 100% DPI profile.

## Package

```powershell
.\build\Package-WindowsNative.ps1 -Version v0.9.3-experimental
.\build\Build-WindowsNativeInstaller.ps1 -Version v0.9.3-experimental
```

The ZIP, setup executable, and SHA-256 files are written under `dist/`.

## GitHub Actions

`.github/workflows/windows-native-release.yml` publishes the .NET 10 self-contained application and builds the Windows package. Tags matching `windows-native-v*` create a GitHub release.

```powershell
git tag windows-native-v0.9.3
git push origin windows-native-v0.9.3
```

Do not create a tag until the runtime self-test, VIIPER smoke test, package test, and installer test all pass.
