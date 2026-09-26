; Inno Setup script for FileRedact.
; Build with:  installer\build-installer.ps1   (publishes the app, then compiles this script)
; or directly: ISCC.exe /DAppVersion=0.1.0 /DPublishDir=..\publish\FileRedact installer\FileRedact.iss

#ifndef AppVersion
  #define AppVersion "0.1.0"
#endif
#ifndef PublishDir
  #define PublishDir "..\publish\FileRedact"
#endif
#ifndef OutputDir
  #define OutputDir "..\publish"
#endif

#define AppName "FileRedact"
#define AppPublisher "Wyatt Rossell"
#define AppURL "https://github.com/wyattrossell/FileRedact"
#define AppExeName "FileRedact.exe"
#define AppGuid "7D4E1F0A-9C3B-4B6E-A2F1-5F0C8E2D1B77"

[Setup]
AppId={{{#AppGuid}}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher={#AppPublisher}
AppPublisherURL={#AppURL}
AppSupportURL={#AppURL}/issues
AppUpdatesURL={#AppURL}/releases
VersionInfoVersion={#AppVersion}
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
UninstallDisplayIcon={app}\{#AppExeName}
UninstallDisplayName={#AppName}
; Per-user install (no UAC prompt for installing or for automatic updates). The printer script elevates
; itself the one time it is needed. Administrators can still install for everyone with /ALLUSERS.
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=commandline
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.17763
OutputDir={#OutputDir}
OutputBaseFilename={#AppName}-Setup-{#AppVersion}
SetupIconFile=..\src\FileRedact.App\Assets\FileRedact.ico
Compression=lzma2/ultra64
SolidCompression=yes
LZMAUseSeparateProcess=yes
WizardStyle=modern
WizardSizePercent=110
ShowLanguageDialog=no
CloseApplications=yes
RestartApplications=no
LicenseFile=
InfoBeforeFile=

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon";    Description: "Create a &desktop shortcut"; GroupDescription: "Shortcuts:"
Name: "installprinter"; Description: "Install the ""FileRedact"" &printer (print from any program straight into FileRedact)"; GroupDescription: "Printer:"
Name: "autostart";      Description: "Start the FileRedact print &watcher when you sign in (required for the printer to open documents automatically)"; GroupDescription: "Printer:"
Name: "contextmenu";    Description: "Add ""&Redact with FileRedact"" to the right-click menu of PDF, Word and image files"; GroupDescription: "Explorer integration:"

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\tools\Install-FileRedactPrinter.ps1"; DestDir: "{app}\tools"; Flags: ignoreversion
Source: "..\README.md"; DestDir: "{app}"; Flags: ignoreversion isreadme
Source: "..\samples\sample-incident-report.txt"; DestDir: "{app}\samples"; Flags: ignoreversion

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExeName}"; Comment: "Redact personal information from documents"
Name: "{group}\Install or repair the FileRedact printer"; Filename: "powershell.exe"; Parameters: "-NoProfile -ExecutionPolicy Bypass -File ""{app}\tools\Install-FileRedactPrinter.ps1"""; IconFilename: "{app}\{#AppExeName}"; Flags: runmaximized
Name: "{group}\Uninstall {#AppName}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExeName}"; Tasks: desktopicon

[Registry]
; Start the background watcher at sign-in so printed documents open automatically.
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "FileRedactWatcher"; ValueData: """{app}\{#AppExeName}"" --watch"; Flags: uninsdeletevalue; Tasks: autostart
; "Redact with FileRedact" context-menu verb (does not change the default program for these files).
; The "image" SystemFileAssociations group covers .png/.jpg/.jpeg/.tif/.tiff/.bmp in one go.
Root: HKA; Subkey: "Software\Classes\SystemFileAssociations\.pdf\shell\FileRedact";          ValueType: string; ValueName: ""; ValueData: "Redact with FileRedact"; Flags: uninsdeletekey; Tasks: contextmenu
Root: HKA; Subkey: "Software\Classes\SystemFileAssociations\.pdf\shell\FileRedact";          ValueType: string; ValueName: "Icon"; ValueData: """{app}\{#AppExeName}"""; Tasks: contextmenu
Root: HKA; Subkey: "Software\Classes\SystemFileAssociations\.pdf\shell\FileRedact\command";  ValueType: string; ValueName: ""; ValueData: """{app}\{#AppExeName}"" ""%1"""; Tasks: contextmenu
Root: HKA; Subkey: "Software\Classes\SystemFileAssociations\.docx\shell\FileRedact";         ValueType: string; ValueName: ""; ValueData: "Redact with FileRedact"; Flags: uninsdeletekey; Tasks: contextmenu
Root: HKA; Subkey: "Software\Classes\SystemFileAssociations\.docx\shell\FileRedact";         ValueType: string; ValueName: "Icon"; ValueData: """{app}\{#AppExeName}"""; Tasks: contextmenu
Root: HKA; Subkey: "Software\Classes\SystemFileAssociations\.docx\shell\FileRedact\command"; ValueType: string; ValueName: ""; ValueData: """{app}\{#AppExeName}"" ""%1"""; Tasks: contextmenu
Root: HKA; Subkey: "Software\Classes\SystemFileAssociations\.doc\shell\FileRedact";          ValueType: string; ValueName: ""; ValueData: "Redact with FileRedact"; Flags: uninsdeletekey; Tasks: contextmenu
Root: HKA; Subkey: "Software\Classes\SystemFileAssociations\.doc\shell\FileRedact";          ValueType: string; ValueName: "Icon"; ValueData: """{app}\{#AppExeName}"""; Tasks: contextmenu
Root: HKA; Subkey: "Software\Classes\SystemFileAssociations\.doc\shell\FileRedact\command";  ValueType: string; ValueName: ""; ValueData: """{app}\{#AppExeName}"" ""%1"""; Tasks: contextmenu
Root: HKA; Subkey: "Software\Classes\SystemFileAssociations\.rtf\shell\FileRedact";          ValueType: string; ValueName: ""; ValueData: "Redact with FileRedact"; Flags: uninsdeletekey; Tasks: contextmenu
Root: HKA; Subkey: "Software\Classes\SystemFileAssociations\.rtf\shell\FileRedact";          ValueType: string; ValueName: "Icon"; ValueData: """{app}\{#AppExeName}"""; Tasks: contextmenu
Root: HKA; Subkey: "Software\Classes\SystemFileAssociations\.rtf\shell\FileRedact\command";  ValueType: string; ValueName: ""; ValueData: """{app}\{#AppExeName}"" ""%1"""; Tasks: contextmenu
Root: HKA; Subkey: "Software\Classes\SystemFileAssociations\.txt\shell\FileRedact";          ValueType: string; ValueName: ""; ValueData: "Redact with FileRedact"; Flags: uninsdeletekey; Tasks: contextmenu
Root: HKA; Subkey: "Software\Classes\SystemFileAssociations\.txt\shell\FileRedact";          ValueType: string; ValueName: "Icon"; ValueData: """{app}\{#AppExeName}"""; Tasks: contextmenu
Root: HKA; Subkey: "Software\Classes\SystemFileAssociations\.txt\shell\FileRedact\command";  ValueType: string; ValueName: ""; ValueData: """{app}\{#AppExeName}"" ""%1"""; Tasks: contextmenu
Root: HKA; Subkey: "Software\Classes\SystemFileAssociations\image\shell\FileRedact";         ValueType: string; ValueName: ""; ValueData: "Redact with FileRedact"; Flags: uninsdeletekey; Tasks: contextmenu
Root: HKA; Subkey: "Software\Classes\SystemFileAssociations\image\shell\FileRedact";         ValueType: string; ValueName: "Icon"; ValueData: """{app}\{#AppExeName}"""; Tasks: contextmenu
Root: HKA; Subkey: "Software\Classes\SystemFileAssociations\image\shell\FileRedact\command"; ValueType: string; ValueName: ""; ValueData: """{app}\{#AppExeName}"" ""%1"""; Tasks: contextmenu

[Run]
; Install the virtual printer. The script asks for administrator approval itself (one UAC prompt). Skipped when
; the printer already exists, so silent upgrades never prompt.
Filename: "powershell.exe"; Parameters: "-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File ""{app}\tools\Install-FileRedactPrinter.ps1"""; StatusMsg: "Installing the FileRedact printer..."; Flags: runhidden waituntilterminated; Tasks: installprinter; Check: NeedPrinterInstall
Filename: "{app}\{#AppExeName}"; Description: "Launch {#AppName}"; Flags: nowait postinstall skipifsilent
; Silent self-update started by the app: bring FileRedact back once the files are in place.
Filename: "{app}\{#AppExeName}"; Flags: nowait; Check: WantAutoRestart

[UninstallRun]
Filename: "powershell.exe"; Parameters: "-NoProfile -ExecutionPolicy Bypass -File ""{app}\tools\Install-FileRedactPrinter.ps1"" -Uninstall"; Flags: runhidden waituntilterminated; RunOnceId: "RemovePrinter"

[UninstallDelete]
Type: filesandordirs; Name: "{app}\tools"
Type: filesandordirs; Name: "{app}\samples"

[Code]
// FileRedact keeps running in the notification area, so make sure it is closed before files are replaced or removed.
procedure KillRunningApp;
var
  ResultCode: Integer;
begin
  Exec(ExpandConstant('{sys}\taskkill.exe'), '/IM {#AppExeName} /F /T', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
end;

// Version 1.0.0 installed per-machine (Program Files, admin). Later versions install per-user so updates
// never need administrator approval. When an old per-machine copy is found, remove it first (one last UAC
// prompt) so the user does not end up with two copies.
procedure MigrateFromPerMachineInstall;
var
  UninstallKey, UninstallExe: String;
  ResultCode: Integer;
begin
  if IsAdminInstallMode then Exit;
  UninstallKey := 'Software\Microsoft\Windows\CurrentVersion\Uninstall\{{#AppGuid}}_is1';
  if RegQueryStringValue(HKEY_LOCAL_MACHINE, UninstallKey, 'UninstallString', UninstallExe) then
  begin
    UninstallExe := RemoveQuotes(UninstallExe);
    if FileExists(UninstallExe) then
    begin
      Log('Removing previous per-machine installation: ' + UninstallExe);
      ShellExec('runas', UninstallExe, '/VERYSILENT /SUPPRESSMSGBOXES /NORESTART', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
      Log('Previous uninstaller exit code: ' + IntToStr(ResultCode));
    end;
  end;
end;

function InitializeSetup(): Boolean;
begin
  KillRunningApp;
  MigrateFromPerMachineInstall;
  Result := True;
end;

// True when the FileRedact printer is not yet installed on this machine.
function NeedPrinterInstall(): Boolean;
begin
  Result := not RegKeyExists(HKEY_LOCAL_MACHINE, 'SYSTEM\CurrentControlSet\Control\Print\Printers\{#AppName}');
end;

// The application passes /AUTORESTART=1 when it runs the installer for a silent self-update.
function WantAutoRestart(): Boolean;
begin
  Result := ExpandConstant('{param:AUTORESTART|0}') = '1';
end;

function InitializeUninstall(): Boolean;
begin
  KillRunningApp;
  Result := True;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if (CurUninstallStep = usPostUninstall) and not UninstallSilent then
  begin
    // Per-user data (settings, received print jobs) is kept unless the user opts to remove it.
    if MsgBox('Also delete FileRedact settings and received print jobs from your user profile?', mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDYES then
      DelTree(ExpandConstant('{localappdata}\FileRedact'), True, True, True);
  end;
end;
