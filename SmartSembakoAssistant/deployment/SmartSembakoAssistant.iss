#ifndef MyAppVersion
#define MyAppVersion "5.0.0"
#endif
#define MyAppName "Smart Sembako Assistant"
#define MyAppPublisher "SA TECH.Inc"
#define MyAppURL "https://github.com/Syarifiin10/Smart-Sembako-Assistant"
#define MyAppExeName "SmartSembakoAssistant.exe"
#ifndef MyAppSourceDir
#define MyAppSourceDir "..\artifacts\staging\publish\win-x64"
#endif

[Setup]
AppId={{3D43F437-8A54-4D99-95D0-86344E14A6E7}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} v{#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
AppUpdatesURL={#MyAppURL}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
DisableDirPage=no
LicenseFile=LICENSE.txt
InfoAfterFile=INSTALL_NOTES.txt
OutputDir=..\artifacts\installer
OutputBaseFilename={#MyAppName}-Setup-v{#MyAppVersion}
SetupIconFile=..\Resources\logo.ico
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=admin
CloseApplications=yes
RestartApplications=no
AppMutex=Local\SmartSembakoAssistant.SingleInstance
UsePreviousAppDir=yes
UninstallDisplayIcon={app}\{#MyAppExeName}
SetupLogging=yes
VersionInfoVersion={#MyAppVersion}
VersionInfoCompany={#MyAppPublisher}
VersionInfoDescription={#MyAppName}
VersionInfoCopyright=Copyright (c) 2026 {#MyAppPublisher}

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Buat shortcut di Desktop"; GroupDescription: "Shortcut tambahan:"
Name: "startmenu"; Description: "Buat shortcut di Start Menu"; GroupDescription: "Shortcut tambahan:"; Flags: checkedonce

[Files]
Source: "{#MyAppSourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: startmenu
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Jalankan {#MyAppName}"; Flags: nowait postinstall skipifsilent
