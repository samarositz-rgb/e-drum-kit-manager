; Installer for E-Drum Kit Manager (Inno Setup 6). Installs for the current user, no admin needed.
#define AppName "E-Drum Kit Manager"
#define AppVersion "1.4.1"

[Setup]
AppId={{6C4A2E31-7B5D-4E3A-9C1F-2D8E5B7A4F10}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher=E-Drum Kit Manager
DefaultDirName={localappdata}\Programs\{#AppName}
DefaultGroupName={#AppName}
PrivilegesRequired=lowest
OutputDir=..\dist
OutputBaseFilename=EDrumKitManager-Setup-{#AppVersion}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
UninstallDisplayIcon={app}\EDrumKitManager.exe
DisableProgramGroupPage=yes
MinVersion=10.0
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
CloseApplications=yes
VersionInfoVersion={#AppVersion}
VersionInfoDescription={#AppName} Setup

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Shortcuts:"

[Files]
Source: "..\dist\app\EDrumKitManager.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "User Guide.html"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\dist\app\NVDA Controller Client license (LGPL 2.1).txt"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\docs\E-Drum Kit Manager Guide.docx"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\EDrumKitManager.exe"
Name: "{group}\{#AppName} User Guide"; Filename: "{app}\User Guide.html"
Name: "{group}\{#AppName} Complete Guide (Word)"; Filename: "{app}\E-Drum Kit Manager Guide.docx"
Name: "{group}\Uninstall {#AppName}"; Filename: "{uninstallexe}"
Name: "{userdesktop}\{#AppName}"; Filename: "{app}\EDrumKitManager.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\User Guide.html"; Description: "Read the user guide"; Flags: postinstall shellexec skipifsilent unchecked
Filename: "{app}\EDrumKitManager.exe"; Description: "Start {#AppName}"; Flags: postinstall nowait skipifsilent
; After an update from inside the program (silent, /RELAUNCH), open it again.
Filename: "{app}\EDrumKitManager.exe"; Flags: nowait; Check: ShouldRelaunch

[UninstallRun]
; Takes the REAPER helper script out of REAPER's startup. Kits and settings stay in AppData.
Filename: "{app}\EDrumKitManager.exe"; Parameters: "--uninstall-reaper-bridge"; Flags: runhidden waituntilterminated; RunOnceId: "RemoveReaperBridge"

[Code]
function ShouldRelaunch: Boolean;
begin
  Result := WizardSilent and (Pos('/RELAUNCH', UpperCase(GetCmdTail)) > 0);
end;
