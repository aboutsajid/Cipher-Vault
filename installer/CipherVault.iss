#define MyAppName "Cipher Vault"
#define MyAppVersion "1.0.0"
#define MyAppPublisher "Cipher"
#define MyAppExeName "CipherVault.exe"

[Setup]
AppId={{A7F10A53-6A79-42F2-BCC5-DC37D63E09E7}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\Cipher Vault
DefaultGroupName={#MyAppName}
AllowNoIcons=yes
OutputDir=D:\CipherVault\artifacts\installer
OutputBaseFilename=CipherVault-Setup
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=admin
ArchitecturesInstallIn64BitMode=x64compatible
DisableProgramGroupPage=yes
UninstallDisplayIcon={app}\{#MyAppExeName}

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "D:\CipherVault\artifacts\publish\win-x64-single\CipherVault.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "D:\CipherVault\artifacts\publish\win-x64-single\BrowserExtension\*"; DestDir: "{app}\BrowserExtension"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent
