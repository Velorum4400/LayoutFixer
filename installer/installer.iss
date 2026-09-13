#define MyAppName "LayoutFixer"
#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif
#define MyAppPublisher "Velorum4400"
#define MyAppExeName "LayoutFixer.exe"

[Setup]
AppId={{6D8A9A9C-BAA3-4C70-B944-09E8D3C82E71}
AppName={#MyAppName}
AppVersion={#AppVersion}
AppVerName={#MyAppName} {#AppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\LayoutFixer
DefaultGroupName=LayoutFixer
DisableProgramGroupPage=yes
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir=output
OutputBaseFilename=LayoutFixer-Setup-{#AppVersion}
SetupIconFile=..\Assets\LayoutFixer.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
CloseApplications=yes
RestartApplications=no
VersionInfoVersion={#AppVersion}
VersionInfoProductName={#MyAppName}
VersionInfoDescription=LayoutFixer installer
VersionInfoCompany={#MyAppPublisher}

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "russian"; MessagesFile: "compiler:Languages\Russian.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "..\dist\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\LayoutFixer"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"
Name: "{autodesktop}\LayoutFixer"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,LayoutFixer}"; WorkingDir: "{app}"; Flags: nowait postinstall skipifsilent
