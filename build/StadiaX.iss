#define MyAppName "Stadia X"
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
AppId={{06D9D28C-4AD8-461C-9C63-5A88CB8FD5C1}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher=Stadia X
AppPublisherURL=https://github.com/jkid92/Stadia-X
AppSupportURL=https://github.com/jkid92/Stadia-X/issues
AppUpdatesURL=https://github.com/jkid92/Stadia-X/releases
DefaultDirName={localappdata}\Programs\Stadia X
DefaultGroupName=Stadia X
DisableProgramGroupPage=yes
LicenseFile={#SourceDir}\LICENSE.txt
OutputDir={#OutputDir}
OutputBaseFilename=Stadia-X-{#MyAppVersion}-Setup
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
UninstallDisplayName=Stadia X
UninstallDisplayIcon={app}\Start-GUI.bat
CloseApplications=no

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Additional shortcuts:"; Flags: unchecked

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs; Excludes: "source\*"

[Icons]
Name: "{group}\Stadia X"; Filename: "{app}\Start-GUI.bat"; WorkingDir: "{app}"
Name: "{autodesktop}\Stadia X"; Filename: "{app}\Start-GUI.bat"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
Filename: "{app}\Start-GUI.bat"; Description: "Launch Stadia X Control Center"; Flags: postinstall shellexec nowait skipifsilent unchecked
