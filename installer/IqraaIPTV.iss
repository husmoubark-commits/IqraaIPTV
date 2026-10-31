#define MyAppName "Iqraa IPTV"
#define MyAppVersion "3.20.0"
#define MyAppPublisher "Iqraa IPTV"
#define MyAppExeName "VoltixIptvWindows.exe"
#define MyAppId "{{B7B4E0D4-4CF8-4B2B-BD1E-9C2E6F5D0A31}"

[Setup]
AppId={#MyAppId}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\Iqraa IPTV
DefaultGroupName={#MyAppName}
OutputDir=installer-output
OutputBaseFilename=IqraaIPTV-Windows-Setup
Compression=lzma2
SolidCompression=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64
PrivilegesRequired=admin
UninstallDisplayIcon={app}\{#MyAppExeName}
DisableProgramGroupPage=yes
WizardStyle=modern

[Files]
Source: "..\windows-player\bin\Release\net8.0-windows\win-x64\publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Tasks]
Name: "desktopicon"; Description: "إنشاء اختصار على سطح المكتب"; GroupDescription: "اختصارات إضافية:"

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "تشغيل {#MyAppName}"; Flags: nowait postinstall skipifsilent
