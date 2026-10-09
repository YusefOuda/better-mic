#define AppName "Better Mic"
#define AppPublisher "Yusef Ouda"
#define AppURL "https://github.com/YusefOuda/better-mic"
#define AppExeName "BetterMic.exe"
#define VbCableURL "https://vb-audio.com/Cable/"

[Setup]
AppId={{9C2E4B71-5A3D-4F08-B6E2-7D1A9F3C8E54}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher={#AppPublisher}
AppPublisherURL={#AppURL}
AppSupportURL={#AppURL}/issues
AppUpdatesURL={#AppURL}/releases
DefaultDirName={localappdata}\better-mic
DisableDirPage=yes
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir=publish
OutputBaseFilename=better-mic-setup
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
UninstallDisplayIcon={app}\{#AppExeName}

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Files]
Source: "publish\{#AppExeName}"; DestDir: "{app}"; Flags: ignoreversion
Source: "publish\micproc.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "THIRD_PARTY_NOTICES.md"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{userstartmenu}\{#AppName}"; Filename: "{app}\{#AppExeName}"

[Run]
Filename: "{app}\{#AppExeName}"; Description: "Launch {#AppName}"; Flags: nowait postinstall skipifsilent

[UninstallRun]
Filename: "taskkill"; Parameters: "/f /im {#AppExeName}"; Flags: runhidden waituntilterminated; RunOnceId: "StopApp"

[Code]
// VB-Cable's license doesn't allow installing it from another setup program, so this page only
// checks for it and points the user to vb-audio.com. The app itself starts processing as soon as
// the device appears, so finishing without it is fine.
const
  CableReady = 0;
  CableMissing = 1;
  CableDisabled = 2;

var
  CablePage: TWizardPage;
  CableStatus: TNewStaticText;

function CheckCable(): Integer;
var
  ResultCode: Integer;
begin
  if Exec(ExpandConstant('{app}\{#AppExeName}'), '--check-cable', '', SW_HIDE, ewWaitUntilTerminated, ResultCode) then
    Result := ResultCode
  else
    Result := CableMissing;
end;

procedure ShowCableStatus(State: Integer);
begin
  case State of
    CableReady:
      CableStatus.Caption := 'VB-Cable found. You''re all set.';
    CableDisabled:
      CableStatus.Caption := 'VB-Cable is installed, but its playback device is disabled. ' +
        'Click "Open Sound settings", right-click "Speakers (VB-Audio Virtual Cable)" under Playback and choose Enable.';
  else
    CableStatus.Caption := 'VB-Cable not found yet. After installing it, click "Check again". ' +
      'If it still isn''t found, restart Windows (VB-Cable asks for a restart).';
  end;
end;

procedure DownloadClick(Sender: TObject);
var
  ErrorCode: Integer;
begin
  ShellExec('open', '{#VbCableURL}', '', '', SW_SHOWNORMAL, ewNoWait, ErrorCode);
end;

procedure SoundSettingsClick(Sender: TObject);
var
  ErrorCode: Integer;
begin
  ShellExec('open', 'control.exe', 'mmsys.cpl,,0', '', SW_SHOWNORMAL, ewNoWait, ErrorCode);
end;

procedure CheckAgainClick(Sender: TObject);
begin
  ShowCableStatus(CheckCable());
end;

function NewButton(Caption: String; Left: Integer; Top: Integer; OnClick: TNotifyEvent): TNewButton;
begin
  Result := TNewButton.Create(CablePage);
  Result.Parent := CablePage.Surface;
  Result.Caption := Caption;
  Result.Left := Left;
  Result.Top := Top;
  Result.Width := ScaleX(150);
  Result.Height := ScaleY(25);
  Result.OnClick := OnClick;
end;

procedure InitializeWizard();
var
  Info: TNewStaticText;
  Top: Integer;
begin
  CablePage := CreateCustomPage(wpInfoAfter, 'One more step: VB-Cable',
    'Better Mic sends your cleaned-up mic to a virtual device provided by VB-Cable.');

  Info := TNewStaticText.Create(CablePage);
  Info.Parent := CablePage.Surface;
  Info.WordWrap := True;
  Info.Width := CablePage.SurfaceWidth;
  Info.Caption :=
    'VB-Cable is a free (donationware) virtual audio driver by VB-Audio. It has to be installed ' +
    'separately:' + #13#10#13#10 +
    '1. Click "Download VB-Cable" and download the Windows driver pack.' + #13#10 +
    '2. Extract the zip, right-click VBCABLE_Setup_x64.exe and choose "Run as administrator".' + #13#10 +
    '3. Click "Install Driver", then come back here and click "Check again".' + #13#10#13#10 +
    'You can also finish now and install VB-Cable later; Better Mic starts working as soon as it appears.';
  Top := Info.Top + Info.Height + ScaleY(16);

  NewButton('Download VB-Cable', 0, Top, @DownloadClick);
  NewButton('Open Sound settings', ScaleX(160), Top, @SoundSettingsClick);
  NewButton('Check again', ScaleX(320), Top, @CheckAgainClick);

  CableStatus := TNewStaticText.Create(CablePage);
  CableStatus.Parent := CablePage.Surface;
  CableStatus.WordWrap := True;
  CableStatus.Width := CablePage.SurfaceWidth;
  CableStatus.Top := Top + ScaleY(40);
  CableStatus.Font.Style := [fsBold];
end;

function ShouldSkipPage(PageID: Integer): Boolean;
var
  State: Integer;
begin
  Result := False;
  if PageID = CablePage.ID then
  begin
    State := CheckCable();
    Result := State = CableReady;
    if not Result then ShowCableStatus(State);
  end;
end;

// Remove the "Start with Windows" entry the app may have created
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usPostUninstall then
    RegDeleteValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Run', 'better-mic');
end;
