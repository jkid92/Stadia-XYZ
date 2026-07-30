#ifndef MyAppName
#define MyAppName "Stadia X Windows Native"
#endif
#ifndef MyAppId
#define MyAppId "{{BB64BA63-E156-47D9-B4FC-F79E384419C3}"
#endif
#ifndef MyInstallDirName
#define MyInstallDirName MyAppName
#endif
#ifndef MyShortcutName
#define MyShortcutName MyAppName
#endif
#ifndef MyOutputPrefix
#define MyOutputPrefix "Stadia-X-Windows-Native"
#endif
#ifndef MyAppVersion
#define MyAppVersion "local"
#endif
#ifndef SourceDir
#define SourceDir ".."
#endif
#ifndef OutputDir
#define OutputDir "..\dist"
#endif

[Setup]
AppId={#MyAppId}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher=Stadia X
AppPublisherURL=https://github.com/jkid92/Stadia-XYZ
AppSupportURL=https://github.com/jkid92/Stadia-XYZ/issues
AppUpdatesURL=https://github.com/jkid92/Stadia-XYZ/releases
DefaultDirName={localappdata}\Programs\{#MyInstallDirName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
LicenseFile={#SourceDir}\LICENSE.txt
OutputDir={#OutputDir}
OutputBaseFilename={#MyOutputPrefix}-{#MyAppVersion}-Setup
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
UninstallDisplayName={#MyAppName}
UninstallDisplayIcon={app}\assets\StadiaX-WindowsNative.ico
SetupIconFile={#SourceDir}\assets\StadiaX-WindowsNative.ico
CloseApplications=no
RestartIfNeededByRun=yes

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Additional shortcuts:"

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Excludes: "logs\*"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "{#SourceDir}\Install-Prerequisites.ps1"; Flags: dontcopy
Source: "{#SourceDir}\dependencies\HidHide_1.5.230_x64.exe"; Flags: dontcopy
Source: "{#SourceDir}\dependencies\USBip-0.9.7.8-x64.exe"; Flags: dontcopy

[InstallDelete]
; Remove files left by the final pre-VIIPER release during an in-place upgrade.
Type: files; Name: "{app}\ViGEmClient.dll"
Type: files; Name: "{app}\dependencies\ViGEmBus_1.22.0_x64_x86_arm64.exe"

[Icons]
Name: "{group}\{#MyShortcutName}"; Filename: "{app}\StadiaX.exe"; WorkingDir: "{app}"; IconFilename: "{app}\assets\StadiaX-WindowsNative.ico"
Name: "{autodesktop}\{#MyShortcutName}"; Filename: "{app}\StadiaX.exe"; WorkingDir: "{app}"; IconFilename: "{app}\assets\StadiaX-WindowsNative.ico"; Tasks: desktopicon

[Run]
Filename: "{app}\StadiaX.exe"; Description: "Launch {#MyAppName}"; Flags: postinstall shellexec nowait skipifsilent unchecked; Check: CanLaunchAfterInstall

[Code]
var
  PrerequisiteRestartRequired: Boolean;

function IsHidHideInstalled: Boolean;
begin
  Result := FileExists(
    ExpandConstant('{commonpf64}\Nefarius Software Solutions\HidHide\x64\HidHideCLI.exe'));
end;

function ReadVersionPart(var VersionText: String): Integer;
var
  Separator: Integer;
  Part: String;
begin
  Separator := Pos('.', VersionText);
  if Separator > 0 then
  begin
    Part := Copy(VersionText, 1, Separator - 1);
    Delete(VersionText, 1, Separator);
  end
  else
  begin
    Part := VersionText;
    VersionText := '';
  end;
  Result := StrToIntDef(Part, 0);
end;

function IsVersionAtLeast(
  VersionText: String;
  RequiredMajor, RequiredMinor, RequiredBuild, RequiredRevision: Integer): Boolean;
var
  Major, Minor, Build, Revision: Integer;
begin
  Major := ReadVersionPart(VersionText);
  Minor := ReadVersionPart(VersionText);
  Build := ReadVersionPart(VersionText);
  Revision := ReadVersionPart(VersionText);

  Result :=
    (Major > RequiredMajor) or
    ((Major = RequiredMajor) and (Minor > RequiredMinor)) or
    ((Major = RequiredMajor) and (Minor = RequiredMinor) and
      (Build > RequiredBuild)) or
    ((Major = RequiredMajor) and (Minor = RequiredMinor) and
      (Build = RequiredBuild) and (Revision >= RequiredRevision));
end;

function IsUsbipInstalled: Boolean;
var
  UsbipPath, UsbipVersion: String;
begin
  UsbipPath := ExpandConstant('{commonpf64}\USBip\usbip.exe');
  Result :=
    RegKeyExists(HKLM64, 'SYSTEM\CurrentControlSet\Services\usbip2_ude') and
    FileExists(UsbipPath) and
    GetVersionNumbersString(UsbipPath, UsbipVersion) and
    IsVersionAtLeast(UsbipVersion, 0, 9, 7, 8);
end;

function IsSuccessfulDependencyExitCode(const ResultCode: Integer): Boolean;
begin
  Result := (ResultCode = 0) or (ResultCode = 1641) or (ResultCode = 3010);
end;

function InstallRequiredDependencies(var ResultCode: Integer): Boolean;
var
  PowerShellPath, ScriptPath, LogPath, Parameters: String;
begin
  WizardForm.StatusLabel.Caption := 'Installing required controller drivers...';
  ExtractTemporaryFile('Install-Prerequisites.ps1');
  ExtractTemporaryFile('HidHide_1.5.230_x64.exe');
  ExtractTemporaryFile('USBip-0.9.7.8-x64.exe');

  PowerShellPath :=
    ExpandConstant('{sysnative}\WindowsPowerShell\v1.0\powershell.exe');
  ScriptPath := ExpandConstant('{tmp}\Install-Prerequisites.ps1');
  LogPath := ExpandConstant('{app}\logs\prerequisite-install.log');
  Parameters :=
    '-NoProfile -NonInteractive -ExecutionPolicy Bypass -File "' +
    ScriptPath + '" -DependencyDirectory "' + ExpandConstant('{tmp}') +
    '" -LogPath "' + LogPath + '"';
  Log('Requesting one elevated prerequisite installation.');

  Result := ShellExec(
    'runas',
    PowerShellPath,
    Parameters,
    ExpandConstant('{app}'),
    SW_HIDE,
    ewWaitUntilTerminated,
    ResultCode);

  if not Result then
    Exit;
  Log('Prerequisite installer exit code: ' + IntToStr(ResultCode));
  if (ResultCode = 1641) or (ResultCode = 3010) then
    PrerequisiteRestartRequired := True;
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  ResultCode: Integer;
begin
  Result := '';

  if (not IsHidHideInstalled) or (not IsUsbipInstalled) then
  begin
    if not InstallRequiredDependencies(ResultCode) then
    begin
      Result :=
        'The required controller drivers could not be installed. ' +
        'Administrator approval is required.';
      Exit;
    end;
    if not IsSuccessfulDependencyExitCode(ResultCode) then
    begin
      Result :=
        'Required controller driver installation failed with exit code ' +
        IntToStr(ResultCode) + '. See prerequisite-install.log for details.';
      Exit;
    end;
  end
  else
    Log('All required controller drivers are already installed.');

  if not IsHidHideInstalled then
  begin
    Result := 'HidHide was not detected after prerequisite installation.';
    Exit;
  end;
  if not IsUsbipInstalled then
  begin
    Result :=
      'usbip-win2 0.9.7.8 was not detected after prerequisite installation.';
    Exit;
  end;

  NeedsRestart := NeedsRestart or PrerequisiteRestartRequired;
  WizardForm.StatusLabel.Caption := 'All required components are installed.';
end;

function CanLaunchAfterInstall: Boolean;
begin
  Result := not PrerequisiteRestartRequired;
end;
