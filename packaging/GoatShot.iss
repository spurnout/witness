#ifndef AppVersion
#define AppVersion "0.1.0"
#endif

#ifndef PublishDir
#define PublishDir "..\artifacts\publish\GoatShot-win-x64"
#endif

#ifndef OutputDir
#define OutputDir "..\artifacts\dist"
#endif

[Setup]
AppId={{4E86AB11-3D28-4D42-BD31-05C48CF751D6}
AppName=GoatShot
AppVersion={#AppVersion}
AppPublisher=GoatShot
AppPublisherURL=https://local.goatshot.invalid
DefaultDirName={localappdata}\Programs\GoatShot
DefaultGroupName=GoatShot
DisableProgramGroupPage=yes
OutputDir={#OutputDir}
OutputBaseFilename=GoatShot-Setup-{#AppVersion}-win-x64
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
UninstallDisplayIcon={app}\GoatShot.exe
SetupLogging=yes

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Shortcuts"; Flags: unchecked
Name: "startup"; Description: "Start GoatShot when I sign in"; GroupDescription: "Startup"; Flags: unchecked

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\GoatShot"; Filename: "{app}\GoatShot.exe"
Name: "{commondesktop}\GoatShot"; Filename: "{app}\GoatShot.exe"; Tasks: desktopicon

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "GoatShot"; ValueData: """{app}\GoatShot.exe"""; Flags: uninsdeletevalue; Tasks: startup

[Run]
Filename: "{app}\GoatShot.exe"; Description: "Launch GoatShot"; Flags: nowait postinstall skipifsilent
