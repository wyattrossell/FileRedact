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

[Setup]
AppId={{7D4E1F0A-9C3B-4B6E-A2F1-5F0C8E2D1B77}
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
; Administrator rights are needed to install the virtual printer.
PrivilegesRequired=admin
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
; Start the background watcher at sign-in so printed documents open automatically. Machine-wide (HKLM) because
; setup runs elevated and the printer itself is machine-wide; the app recognises this entry as "start with Windows".
Root: HKLM; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "FileRedactWatcher"; ValueData: """{app}\{#AppExeName}"" --watch"; Flags: uninsdeletevalue; Tasks: autostart
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
; Install the virtual printer (setup already runs elevated, so no extra UAC prompt).
Filename: "powershell.exe"; Parameters: "-NoProfile -ExecutionPolicy Bypass -File ""{app}\tools\Install-FileRedactPrinter.ps1"""; StatusMsg: "Installing the FileRedact printer..."; Flags: runhidden waituntilterminated; Tasks: installprinter
Filename: "{app}\{#AppExeName}"; Description: "Launch {#AppName}"; Flags: nowait postinstall skipifsilent

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

function InitializeSetup(): Boolean;
begin
  KillRunningApp;
  Result := True;
end;

function InitializeUninstall(): Boolean;
begin
  KillRunningApp;
  Result := True;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usPostUninstall then
  begin
    // Per-user data (settings, received print jobs) is kept unless the user opts to remove it.
    if MsgBox('Also delete FileRedact settings and received print jobs from your user profile?', mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDYES then
      DelTree(ExpandConstant('{localappdata}\FileRedact'), True, True, True);
  end;
end;
