; Script Inno Setup pour KeyCode IDE
; Permet également la compilation via ISCC.exe si Inno Setup 6 est installé.

#define MyAppName "KeyCode"
#define MyAppVersion "2.0.0"
#define MyAppPublisher "Équipe KeyCode (Keyce)"
#define MyAppURL "https://github.com/cabrelds/keycecode"
#define MyAppExeName "KeyCode.exe"
#define PublishDir "..\publish\win-x64"

[Setup]
AppId={{8D6B4A31-5E2A-4D79-91F2-C7A82E1B9A20}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}/issues
DefaultDirName={localappdata}\Programs\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
OutputDir=dist
OutputBaseFilename=KeyCodeSetup-Inno
SetupIconFile=..\KeyCode\Assets\keycode.ico
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern

[Languages]
Name: "french"; MessagesFile: "compiler:Languages\French.isl"

[Tasks]
Name: "desktopicon"; Description: "Créer une icône sur le Bureau"; GroupDescription: "Icônes supplémentaires :"
Name: "contextmenu"; Description: "Ajouter « Ouvrir avec KeyCode » au menu contextuel Windows"; GroupDescription: "Intégration Windows :"

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; IconFilename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; IconFilename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Registry]
Root: HKCU; Subkey: "Software\Classes\*\shell\KeyCode"; ValueType: string; ValueName: ""; ValueData: "Ouvrir avec KeyCode"; Flags: uninsdeletekey; Tasks: contextmenu
Root: HKCU; Subkey: "Software\Classes\*\shell\KeyCode"; ValueType: string; ValueName: "Icon"; ValueData: """{app}\{#MyAppExeName}"""; Tasks: contextmenu
Root: HKCU; Subkey: "Software\Classes\*\shell\KeyCode\command"; ValueType: string; ValueName: ""; ValueData: """{app}\{#MyAppExeName}"" ""%1"""; Tasks: contextmenu
Root: HKCU; Subkey: "Software\Classes\Directory\shell\KeyCode"; ValueType: string; ValueName: ""; ValueData: "Ouvrir le dossier dans KeyCode"; Flags: uninsdeletekey; Tasks: contextmenu
Root: HKCU; Subkey: "Software\Classes\Directory\shell\KeyCode"; ValueType: string; ValueName: "Icon"; ValueData: """{app}\{#MyAppExeName}"""; Tasks: contextmenu
Root: HKCU; Subkey: "Software\Classes\Directory\shell\KeyCode\command"; ValueType: string; ValueName: ""; ValueData: """{app}\{#MyAppExeName}"" ""%V"""; Tasks: contextmenu

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Lancer KeyCode"; Flags: nowait postinstall skipifsilent
