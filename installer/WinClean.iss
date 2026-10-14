; WinClean — Inno Setup installer script (requires Inno Setup 6.3 or later)
;
; Build order:
;   1. powershell -File tools\publish.ps1        -> publish\win-x64\WinClean.exe + rules.json
;   2. ISCC.exe installer\WinClean.iss           -> publish\installer\WinClean-Setup-<version>.exe
;
; The version number is read from the published executable, so it never drifts.
; The app is self-contained: the installer ships exactly WinClean.exe and rules.json.

#define MyAppName "WinClean"
#define MyAppExeName "WinClean.exe"
#define MyAppPublisher "Aakash9969"
#define MyAppURL "https://github.com/Aakash9969/WinClean"
#define PublishDir "../publish/win-x64"

#if FileExists(PublishDir + "/" + MyAppExeName)
    #define FullVersion GetVersionNumbersString(PublishDir + "/" + MyAppExeName)
    #define MyAppVersion Copy(FullVersion, 1, RPos(".", FullVersion) - 1)
#endif
#ifndef MyAppVersion
    #define MyAppVersion "1.0.0"
#endif

[Setup]
AppId={{079F5A7B-5568-4B13-8574-9F9B44681DB3}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
AppUpdatesURL={#MyAppURL}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
PrivilegesRequired=admin
WizardStyle=modern
OutputDir=../publish/installer
OutputBaseFilename=WinClean-Setup-{#MyAppVersion}
SetupIconFile=../src/WinClean.App/winclean.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
Compression=lzma2/max
SolidCompression=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0

#if FileExists("../LICENSE")
LicenseFile=../LICENSE
#endif

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#PublishDir}/WinClean.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#PublishDir}/rules.json"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\{cm:UninstallProgram,{#MyAppName}}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#MyAppName}}"; Flags: nowait postinstall skipifsilent

; User data (logs, history, settings) lives in %LOCALAPPDATA%\WinClean and is deliberately
; NOT removed on uninstall — the app never deletes user data without consent.
