#define MyAppName "Stadia X Windows Native"
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
AppId={{BB64BA63-E156-47D9-B4FC-F79E384419C3}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher=Stadia X
AppPublisherURL=https://github.com/jkid92/Stadia-XYZ
AppSupportURL=https://github.com/jkid92/Stadia-XYZ/issues
AppUpdatesURL=https://github.com/jkid92/Stadia-XYZ/releases
DefaultDirName={localappdata}\Programs\Stadia X Windows Native
DefaultGroupName=Stadia X Windows Native
DisableProgramGroupPage=yes
LicenseFile={#SourceDir}\LICENSE.txt
OutputDir={#OutputDir}
OutputBaseFilename=Stadia-X-Windows-Native-{#MyAppVersion}-Setup
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
UninstallDisplayName=Stadia X Windows Native
UninstallDisplayIcon={app}\StadiaX.exe
SetupIconFile={#SourceDir}\assets\StadiaX.ico
CloseApplications=no

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Additional shortcuts:"

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\Stadia X Windows Native"; Filename: "{app}\StadiaX.exe"; WorkingDir: "{app}"
Name: "{autodesktop}\Stadia X Windows Native"; Filename: "{app}\StadiaX.exe"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
Filename: "{app}\StadiaX.exe"; Description: "Launch Stadia X Windows Native"; Flags: postinstall shellexec nowait skipifsilent unchecked
