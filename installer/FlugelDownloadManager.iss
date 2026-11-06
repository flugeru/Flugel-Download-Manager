; Flugel Download Manager - Inno Setup script. Built by build-installer.ps1 (unsigned unless signed separately).
; The installer copies files only. It never registers a browser extension or native host; see README.md.
#ifndef MyAppVersion
  #define MyAppVersion "0.2.0"
#endif
#define MyAppName "Flugel Download Manager"

[Setup]
AppId={{6F0E3D1B-2B7A-4E0C-9C55-F1A7D2B8E001}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher=Flugel
DefaultDirName={autopf}\Flugel Download Manager
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
LicenseFile=..\LICENSE
OutputDir=..\artifacts\installer
OutputBaseFilename=FlugelDownloadManager-Setup-{#MyAppVersion}
Compression=lzma2
SolidCompression=yes
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
UninstallDisplayName={#MyAppName}
UninstallDisplayIcon={app}\Flugel.Desktop\FlugelDownloadManager.exe
WizardStyle=modern

[Languages]
Name: "brazilianportuguese"; MessagesFile: "compiler:Languages\BrazilianPortuguese.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Criar atalho na área de trabalho"; GroupDescription: "Atalhos:"; Flags: unchecked

[Files]
Source: "..\artifacts\payload\Flugel.Desktop\*"; DestDir: "{app}\Flugel.Desktop"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\artifacts\payload\Flugel.NativeHost\*"; DestDir: "{app}\Flugel.NativeHost"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\browser\extension\*"; DestDir: "{app}\extension"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\browser\Register-NativeHost.ps1"; DestDir: "{app}\tools"; Flags: ignoreversion
Source: "..\browser\Unregister-NativeHost.ps1"; DestDir: "{app}\tools"; Flags: ignoreversion
Source: "..\README.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\LICENSE"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\Flugel.Desktop\FlugelDownloadManager.exe"
Name: "{group}\Desinstalar {#MyAppName}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\Flugel.Desktop\FlugelDownloadManager.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\Flugel.Desktop\FlugelDownloadManager.exe"; Description: "Abrir {#MyAppName}"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
; User downloads, history and settings in %LOCALAPPDATA% are kept on purpose.
Type: filesandordirs; Name: "{app}\Flugel.Desktop"
Type: filesandordirs; Name: "{app}\Flugel.NativeHost"
Type: filesandordirs; Name: "{app}\extension"
Type: filesandordirs; Name: "{app}\tools"
