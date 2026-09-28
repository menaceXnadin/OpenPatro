; OpenPatro installer — Inno Setup 6 script (free: https://jrsoftware.org/isdl.php).
;
; Produces a standard Setup.exe: per-user install (no admin/UAC prompt),
; Start Menu entry, optional desktop icon, working uninstaller.
;
; Build:
;   1. dotnet publish -c Release -p:Platform=x64 /p:PublishProfile=win-x64.pubxml
;   2. ISCC installer\OpenPatro.iss
;   Output: installer\Output\OpenPatroSetup-<version>-x64.exe
;
; Notes:
; - No code-signing cert required. Unsigned Setup.exe triggers a one-time
;   SmartScreen "Unknown publisher" warning on other PCs (More info > Run anyway).
; - Startup-at-boot is NOT handled here: the app registers its own HKCU Run
;   entry (with --startup) on first launch — see Services/StartupService.cs.
;   Uninstall cleans up that registry value (see [Registry] below).
; - User data (%LocalAppData%\OpenPatro\*.db) is intentionally LEFT on disk
;   at uninstall so notes/settings survive reinstalls.
; - Version below must be bumped together with Package.appxmanifest.

#define MyAppName "OpenPatro"
#define MyAppVersion "1.0.0"
#define MyAppPublisher "MenaceXnadin"
#define MyAppExeName "OpenPatro.exe"
#define PublishDir "..\\bin\\win-x64\\publish"

[Setup]
AppId={{e38b3105-4b4f-466f-af46-cfdf079a7e30}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\{#MyAppName}
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir=Output
OutputBaseFilename={#MyAppName}Setup-{#MyAppVersion}-x64
SetupIconFile=..\favicon.ico
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
UninstallDisplayName={#MyAppName}
VersionInfoVersion={#MyAppVersion}

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

; Remove our HKCU Run value on uninstall so Windows doesn't try to launch
; a deleted exe at logon. (The app recreates it when enabled.)
[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueName: "OpenPatro"; Flags: uninsdeletevalue noerror

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#MyAppName}}"; Flags: nowait postinstall

[Code]
// The app idles in the tray (and auto-starts at boot), so its exe is almost
// always locked. Kill it silently before install/uninstall so upgrades never
// hit "file in use" / reboot prompts. taskkill exits nonzero when the process
// isn't running — that's fine, we ignore it.
function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  ResultCode: Integer;
begin
  Exec('taskkill.exe', '/f /im {#MyAppExeName}', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Result := '';
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  ResultCode: Integer;
begin
  if CurUninstallStep = usUninstall then
    Exec('taskkill.exe', '/f /im {#MyAppExeName}', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
end;
