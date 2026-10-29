; Garden Helper installer (Inno Setup 6). Built by scripts\build.ps1, which passes /DAppVersion=... and /DSourceDir=...
#ifndef AppVersion
  #define AppVersion "0.1.0"
#endif
#ifndef SourceDir
  #define SourceDir "..\publish\win-x64"
#endif

#define AppName "Garden Helper"
#define AppExe "GardenHelper.exe"

[Setup]
AppId={{6F1C2B7A-5D3E-4C71-9A0B-7E2D4B8C1F35}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher=Garden Helper contributors
AppPublisherURL=https://github.com/unupunct/Garden-Hepler
AppSupportURL=https://github.com/unupunct/Garden-Hepler/issues
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
OutputDir=..\dist
OutputBaseFilename=GardenHelper-Setup-{#AppVersion}
SetupIconFile=..\src\GardenHelper.App\Assets\GardenHelper.ico
UninstallDisplayIcon={app}\{#AppExe}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=admin
PrivilegesRequiredOverridesAllowed=dialog
MinVersion=10.0
LicenseFile=..\LICENSE
VersionInfoVersion={#AppVersion}
VersionInfoProductName={#AppName}
VersionInfoDescription={#AppName} setup
CloseApplications=yes

[Languages]
; Inno Setup ships no official Romanian wizard translation; the app itself is fully bilingual.
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\README.md"; DestDir: "{app}\docs"; Flags: ignoreversion
Source: "..\CHANGELOG.md"; DestDir: "{app}\docs"; Flags: ignoreversion
Source: "..\THIRD-PARTY-NOTICES.md"; DestDir: "{app}\docs"; Flags: ignoreversion
Source: "..\docs\USER_GUIDE.md"; DestDir: "{app}\docs"; Flags: ignoreversion
Source: "..\LICENSE"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExe}"
Name: "{group}\{#AppName} user guide"; Filename: "{app}\docs\USER_GUIDE.md"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExe}"; Description: "{cm:LaunchProgram,{#AppName}}"; Flags: nowait postinstall skipifsilent

; User data lives in %LOCALAPPDATA%\GardenHelper and is deliberately NOT listed under [UninstallDelete]:
; uninstalling or upgrading never removes the user's garden, library or backups.
