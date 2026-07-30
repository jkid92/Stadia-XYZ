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

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Additional shortcuts:"

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Excludes: "logs\*"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#MyShortcutName}"; Filename: "{app}\StadiaX.exe"; WorkingDir: "{app}"; IconFilename: "{app}\assets\StadiaX-WindowsNative.ico"
Name: "{autodesktop}\{#MyShortcutName}"; Filename: "{app}\StadiaX.exe"; WorkingDir: "{app}"; IconFilename: "{app}\assets\StadiaX-WindowsNative.ico"; Tasks: desktopicon

[Run]
Filename: "{app}\StadiaX.exe"; Description: "Launch {#MyAppName}"; Flags: postinstall shellexec nowait skipifsilent unchecked
