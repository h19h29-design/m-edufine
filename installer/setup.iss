#ifndef AppVersion
 #define AppVersion "0.2.0"
#endif
[Setup]
AppId={{9DD29815-B04A-4D2E-8CE8-C90085AC8E09}
AppName=M-Edufine
AppVersion={#AppVersion}
AppPublisher=h19h29-design
AppPublisherURL=https://github.com/h19h29-design/m-edufine
DefaultDirName={localappdata}\Programs\M-Edufine
DefaultGroupName=M-Edufine
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir=..\artifacts
OutputBaseFilename=M-Edufine-Setup-{#AppVersion}-win-x64
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
CloseApplications=yes
RestartApplications=no
UninstallDisplayIcon={app}\M-Edufine.exe
SetupLogging=yes
[Files]
Source: "..\publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
[Icons]
Name: "{group}\M-Edufine"; Filename: "{app}\M-Edufine.exe"
Name: "{group}\Extension folder"; Filename: "{app}\extension"
Name: "{autodesktop}\M-Edufine"; Filename: "{app}\M-Edufine.exe"; Tasks: desktopicon
[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; Flags: unchecked
[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Edge\NativeMessagingHosts\com.medufine.bridge"; ValueType: string; ValueName: ""; ValueData: "{app}\native-host.json"; Flags: uninsdeletekey
Root: HKCU; Subkey: "Software\Google\Chrome\NativeMessagingHosts\com.medufine.bridge"; ValueType: string; ValueName: ""; ValueData: "{app}\native-host.json"; Flags: uninsdeletekey
[Run]
Filename: "{app}\M-Edufine.exe"; Description: "Launch M-Edufine (F1: first-time extension setup)"; Flags: nowait postinstall skipifsilent
