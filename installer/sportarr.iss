; Sportarr Windows Installer Script
; Built with Inno Setup (https://jrsoftware.org/isinfo.php)

#define MyAppName "Sportarr"
#define MyAppPublisher "Sportarr"
#define MyAppURL "https://sportarr.net"
#define MyAppExeName "Sportarr.exe"
#define MyAppDescription "Sports PVR for Usenet and Torrents"

; Version is passed via command line: /DMyAppVersion=1.0.0.100
#ifndef MyAppVersion
  #define MyAppVersion "1.0.0.0"
#endif

[Setup]
; NOTE: The value of AppId uniquely identifies this application.
AppId={{8A9E7D4B-3C1F-4E5A-9B2D-6F8A0C3E1D5B}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
AppUpdatesURL=https://github.com/Sportarr/Sportarr/releases
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
AllowNoIcons=yes
LicenseFile=..\COPYRIGHT.md
OutputDir=..\installer-output
OutputBaseFilename=Sportarr-Setup-{#MyAppVersion}
SetupIconFile=..\Logo\sportarr.ico
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
UninstallDisplayIcon={app}\{#MyAppExeName}
UninstallDisplayName={#MyAppName}
VersionInfoVersion={#MyAppVersion}
VersionInfoCompany={#MyAppPublisher}
VersionInfoDescription={#MyAppDescription}
VersionInfoProductName={#MyAppName}
MinVersion=10.0

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked
Name: "quicklaunchicon"; Description: "{cm:CreateQuickLaunchIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked; OnlyBelowVersion: 6.1; Check: not IsAdminInstallMode
Name: "startupicon"; Description: "Start Sportarr when Windows starts"; GroupDescription: "Startup:"
Name: "installservice"; Description: "Install as Windows Service (runs in background)"; GroupDescription: "Service:"; Flags: unchecked

[Files]
; Main application files from publish directory
Source: "..\publish\win-x64\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
; Data directory marker
Source: "..\installer\data-readme.txt"; DestDir: "{app}\data"; Flags: ignoreversion; Check: not DirExists(ExpandConstant('{app}\data'))

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\{#MyAppName} (Tray Mode)"; Filename: "{app}\{#MyAppExeName}"; Parameters: "--tray"
Name: "{group}\{cm:UninstallProgram,{#MyAppName}}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon
Name: "{userappdata}\Microsoft\Internet Explorer\Quick Launch\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: quicklaunchicon
Name: "{userstartup}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Parameters: "--tray"; Tasks: startupicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent
Filename: "{app}\{#MyAppExeName}"; Parameters: "--tray"; Description: "Launch {#MyAppName} minimized to tray"; Flags: nowait postinstall skipifsilent unchecked

[UninstallRun]
; Stop the service if it's running
Filename: "sc"; Parameters: "stop Sportarr"; Flags: runhidden; RunOnceId: "StopService"
Filename: "sc"; Parameters: "delete Sportarr"; Flags: runhidden; RunOnceId: "DeleteService"

[UninstallDelete]
; Clean up logs but preserve config/data by default
Type: filesandordirs; Name: "{app}\logs"

[Code]
var
  DataDirPage: TInputDirWizardPage;

procedure InitializeWizard;
begin
  // Custom page for data directory selection
  DataDirPage := CreateInputDirPage(wpSelectDir,
    'Select Data Directory',
    'Where should Sportarr store its configuration and database?',
    'Select the folder where Sportarr will store its configuration, database, and logs.',
    False, '');
  DataDirPage.Add('');
  DataDirPage.Values[0] := ExpandConstant('{autopf}\{#MyAppName}\data');
end;

function GetDataDir(Param: String): String;
begin
  Result := DataDirPage.Values[0];
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  ResultCode: Integer;
  ServiceName: String;
begin
  if CurStep = ssPostInstall then
  begin
    // Install as Windows Service if selected
    if IsTaskSelected('installservice') then
    begin
      ServiceName := 'Sportarr';
      // Create the service
      Exec('sc', 'create ' + ServiceName + ' binPath= "\"' + ExpandConstant('{app}\{#MyAppExeName}') + '\" --service" start= auto DisplayName= "Sportarr"', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
      // Set description
      Exec('sc', 'description ' + ServiceName + ' "Sports PVR for Usenet and Torrents - monitors sports leagues and downloads releases automatically."', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
      // Start the service
      Exec('sc', 'start ' + ServiceName, '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
    end;
  end;
end;

function InitializeUninstall(): Boolean;
var
  ResultCode: Integer;
begin
  Result := True;
  // Try to stop any running instance
  Exec('taskkill', '/F /IM Sportarr.exe', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
end;
