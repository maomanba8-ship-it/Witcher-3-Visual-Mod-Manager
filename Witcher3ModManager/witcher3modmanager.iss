; Witcher3 Mod Manager - Inno Setup Installer Script
; Build: D:\InnoSetup\ISCC.exe witcher3modmanager.iss

#define MyAppName "巫师3 Mod 管理器"
#define MyAppNameEn "Witcher 3 Mod Manager"
#define MyAppVersion "1.0.0"
#define MyAppPublisher "W3MM"
#define MyAppURL "https://github.com/w3mm"
#define MyAppExeName "Witcher3ModManager.exe"

[Setup]
AppId={{B8F4A3D2-7E1C-4F6B-9A2D-5C8E0F1D3B7A}}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppSupportURL={#MyAppURL}
DefaultDirName={autopf}\{#MyAppNameEn}
DefaultGroupName={#MyAppName}
AllowNoIcons=yes
OutputDir=.\installer
OutputBaseFilename=Witcher3ModManager_Setup_v{#MyAppVersion}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=lowest
DisableWelcomePage=no
LicenseFile=..\..\LICENSE.txt
SetupIconFile=..\Resources\Icons\app.ico
UninstallDisplayIcon={app}\{#MyAppExeName}

[Languages]
Name: "chinese"; MessagesFile: "compiler:Languages\ChineseSimplified.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"

[Files]
Source: "..\bin\Release\net10.0-windows\publish\{#MyAppExeName}"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\bin\Release\net10.0-windows\publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
; 7-Zip components (optional - bundle with app)
Source: "D:\7-Zip\7z.exe"; DestDir: "{app}\7zip"; Flags: ignoreversion
Source: "D:\7-Zip\7z.dll"; DestDir: "{app}\7zip"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent
