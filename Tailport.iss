; ============================================================
;  Tailport.iss - Inno Setup script for the Tailport setup wizard
;  Build: build-installer.cmd  (or: ISCC /DAppVersion=x.y.z Tailport.iss)
;
;  The wizard: WSL2 check -> tailnet services (IP + port list)
;  -> installs per-user (no admin) -> optionally runs the WSL2
;  bootstrap on first-time machines.
; ============================================================

#ifndef AppVersion
  #define AppVersion "1.8.0"
#endif

[Setup]
AppId={{7C0C0C61-8E1B-4A33-9D1B-4A1C9E9E9C61}
AppName=Tailport
AppVersion={#AppVersion}
AppPublisher=LittleJakub
AppPublisherURL=https://github.com/LittleJakub/Tailport
AppSupportURL=https://github.com/LittleJakub/Tailport
DefaultDirName={localappdata}\Tailport
DefaultGroupName=Tailport
DisableProgramGroupPage=yes
UninstallDisplayIcon={app}\Tailport.exe
OutputDir=installer
OutputBaseFilename=TailportSetup-{#AppVersion}
Compression=lzma2/ultra64
SolidCompression=yes
PrivilegesRequired=lowest
CloseApplications=yes
SetupLogging=yes
WizardStyle=modern dynamic
LicenseFile=LICENSE

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a &desktop shortcut"; GroupDescription: "Additional icons:"

[Files]
Source: "Tailport.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "Tailport.exe.config"; DestDir: "{app}"; Flags: ignoreversion
Source: "forwarder.py"; DestDir: "{app}"; Flags: ignoreversion
Source: "tailport.config.example"; DestDir: "{app}"; Flags: ignoreversion
Source: "config.cmd"; DestDir: "{app}"; Flags: ignoreversion
Source: "start.cmd"; DestDir: "{app}"; Flags: ignoreversion
Source: "stop.cmd"; DestDir: "{app}"; Flags: ignoreversion
Source: "check.cmd"; DestDir: "{app}"; Flags: ignoreversion
Source: "bootstrap.cmd"; DestDir: "{app}"; Flags: ignoreversion
Source: "bootstrap-wsl.sh"; DestDir: "{app}"; Flags: ignoreversion
Source: "assets\*"; DestDir: "{app}\assets"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\Tailport"; Filename: "{app}\Tailport.exe"
Name: "{group}\WSL2 bootstrap"; Filename: "{app}\bootstrap.cmd"
Name: "{group}\Uninstall Tailport"; Filename: "{uninstallexe}"
Name: "{userdesktop}\Tailport"; Filename: "{app}\Tailport.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\Tailport.exe"; Description: "Launch Tailport"; Flags: nowait postinstall skipifsilent
Filename: "{app}\bootstrap.cmd"; Description: "Run the WSL2 bootstrap now (first-time setup)"; Flags: nowait postinstall skipifsilent; Check: WslNotReady

[Code]
const
  WSL_DISTRO = 'Ubuntu';
  TMP_OUT = '{tmp}\tp_wsl_out.txt';

var
  WslPage: TWizardPage;
  WslStatus: TNewStaticText;
  CfgPage: TWizardPage;
  DistroEdit: TNewEdit;
  IpEdit: TNewEdit;
  ForwardsMemo: TNewMemo;
  WslOk, DistroOk, TailscaleOk: Boolean;
  DetectedPythonw: String;
  OwnIp: String; { this PC's own tailnet IP, when detectable }

{ ---- helpers ------------------------------------------------- }

function RunCapture(const CmdLine: String): Integer;
var
  TmpFile: String;
begin
  TmpFile := ExpandConstant(TMP_OUT);
  Exec('cmd.exe', '/c ' + CmdLine + ' > "' + TmpFile + '" 2>&1', '', SW_HIDE,
       ewWaitUntilTerminated, Result);
end;

function WslInstalled: Boolean;
begin
  Result := RunCapture('wsl.exe --status') = 0;
end;

function DistroAvailable: Boolean;
begin
  Result := RunCapture('wsl.exe -d ' + WSL_DISTRO + ' -u root -- echo ok') = 0;
end;

function TailscaleRunning: Boolean;
begin
  Result := RunCapture('wsl.exe -d ' + WSL_DISTRO + ' -u root -- systemctl is-active tailscaled') = 0;
end;

function WslNotReady: Boolean;
begin
  Result := not TailscaleOk;
end;

{ --- python detection: find a pythonw that can actually run the
      forwarder (imports pysocks). `where pythonw` alone is not
      enough - it may find a venv or Store stub without pysocks. }

function PythonHasSocks(const PyW: String): Boolean;
begin
  Result := RunCapture('"' + PyW + '" -c "import socks"') = 0;
end;

function FirstLineOf(const TmpFile: String): String;
var
  Content: AnsiString;
  P: Integer;
begin
  Result := '';
  if not LoadStringFromFile(TmpFile, Content) then Exit;
  P := Pos(#13#10, Content);
  if P > 0 then Result := Copy(Content, 1, P - 1)
  else if Trim(Content) <> '' then Result := Trim(Content);
end;

function DetectPythonw: String;
var
  TmpFile: String;
  PyExe: String;
begin
  Result := '';
  TmpFile := ExpandConstant(TMP_OUT);

  { 1) the py launcher points at the real interpreter }
  if RunCapture('py -3 -c "import sys;print(sys.executable)"') = 0 then
  begin
    PyExe := FirstLineOf(TmpFile);
    if Pos('python.exe', PyExe) > 0 then
    begin
      Result := Copy(PyExe, 1, Length(PyExe) - Length('python.exe')) + 'pythonw.exe';
      if not PythonHasSocks(Result) then Result := '';
    end;
  end;

  { 2) fallback: pythonw from PATH, but only if pysocks is there }
  if Result = '' then
  begin
    if RunCapture('where pythonw') = 0 then
    begin
      Result := FirstLineOf(TmpFile);
      if (Result <> '') and not PythonHasSocks(Result) then Result := '';
    end;
  end;
end;

{ ---- wizard pages --------------------------------------------- }

procedure InitializeWizard;
var
  Status: String;
  StatusColor: Longint;
begin
  WslOk := WslInstalled;
  DistroOk := False;
  TailscaleOk := False;
  if WslOk then
  begin
    DistroOk := DistroAvailable;
    if DistroOk then TailscaleOk := TailscaleRunning;
  end;
  DetectedPythonw := DetectPythonw;

  { remember this PC's own tailnet IP (when tailscaled is running here)
    so the wizard can warn when the user types it by mistake }
  OwnIp := '';
  if TailscaleOk then
    if RunCapture('wsl.exe -d ' + WSL_DISTRO + ' -u root -- tailscale ip -4') = 0 then
      OwnIp := FirstLineOf(ExpandConstant(TMP_OUT));

  { --- WSL2 check page --- }
  WslPage := CreateCustomPage(wpLicense, 'WSL2 check',
    'The tailnet door lives inside WSL2 - is it ready?');
  WslStatus := TNewStaticText.Create(WslPage);
  WslStatus.Parent := WslPage.Surface;
  WslStatus.Left := ScaleX(12);
  WslStatus.Top := ScaleY(16);
  WslStatus.Width := WslPage.SurfaceWidth - ScaleX(24);
  WslStatus.AutoSize := False;
  WslStatus.WordWrap := True;

  if not WslOk then
  begin
    Status := 'WSL2 is not installed or not enabled.' + #13#10 + #13#10 +
      'Install it first (admin PowerShell):  wsl --install' + #13#10 +
      'then reboot and install a distro:      wsl --install -d Ubuntu' + #13#10 + #13#10 +
      'Tailport will still install - run bootstrap.cmd after WSL2 is ready.';
    StatusColor := $00575757; { amber-grey }
  end
  else if not DistroOk then
  begin
    Status := 'WSL2 is installed, but the distro "' + WSL_DISTRO + '" is missing.' + #13#10 + #13#10 +
      'Install it:  wsl --install -d ' + WSL_DISTRO + #13#10 + #13#10 +
      'Tailport will still install - run bootstrap.cmd after the distro exists.';
    StatusColor := $00575757;
  end
  else if not TailscaleOk then
  begin
    Status := 'WSL2 with ' + WSL_DISTRO + ' is ready, but tailscaled is not set up yet.' + #13#10 + #13#10 +
      'The final step of this wizard offers to run the one-command bootstrap' + #13#10 +
      '(installs tailscaled + SOCKS5 inside WSL2 and logs into Tailscale).';
    StatusColor := $00485EB0; { amber }
  end
  else
  begin
    Status := 'Everything is ready: WSL2 is up, ' + WSL_DISTRO +
      ' exists and tailscaled is running.' + #13#10 + #13#10 +
      'Install Tailport, Turn ON from the tray, and your tailnet services' + #13#10 +
      'answer on localhost.';
    StatusColor := $0048A04F; { green }
  end;
  WslStatus.Caption := Status;
  WslStatus.Font.Color := StatusColor;

  { --- tailnet services page --- }
  CfgPage := CreateCustomPage(WslPage.ID, 'Tailnet services',
    'Which services should localhost open?');

  with TNewStaticText.Create(CfgPage) do
  begin
    Parent := CfgPage.Surface;
    Left := ScaleX(12);
    Top := ScaleY(4);
    Caption := 'WSL distro running tailscaled:';
  end;

  DistroEdit := TNewEdit.Create(CfgPage);
  with DistroEdit do
  begin
    Parent := CfgPage.Surface;
    Left := ScaleX(12);
    Top := ScaleY(24);
    Width := ScaleX(160);
    Text := WSL_DISTRO;
  end;

  with TNewStaticText.Create(CfgPage) do
  begin
    Parent := CfgPage.Surface;
    Left := ScaleX(12);
    Top := ScaleY(68);
    Caption := 'Tailnet IP of the machine running your services:';
  end;

  IpEdit := TNewEdit.Create(CfgPage);
  with IpEdit do
  begin
    Parent := CfgPage.Surface;
    Left := ScaleX(12);
    Top := ScaleY(88);
    Width := ScaleX(220);
    Text := '';
  end;

  with TNewStaticText.Create(CfgPage) do
  begin
    Parent := CfgPage.Surface;
    Left := ScaleX(12);
    Top := ScaleY(114);
    Width := CfgPage.SurfaceWidth - ScaleX(24);
    WordWrap := True;
    Caption := 'NOT this PC - run "tailscale ip -4" on the other machine that runs the services and paste its IP here.';
    Font.Color := $00808080;
  end;

  with TNewStaticText.Create(CfgPage) do
  begin
    Parent := CfgPage.Surface;
    Left := ScaleX(12);
    Top := ScaleY(148);
    Caption := 'Port forwards - one per line: local:tailnet-ip:port';
  end;

  ForwardsMemo := TNewMemo.Create(CfgPage);
  with ForwardsMemo do
  begin
    Parent := CfgPage.Surface;
    Left := ScaleX(12);
    Top := ScaleY(168);
    Width := CfgPage.SurfaceWidth - ScaleX(24);
    Height := ScaleY(84);
    ScrollBars := ssVertical;
    Text := '';
  end;

  with TNewStaticText.Create(CfgPage) do
  begin
    Parent := CfgPage.Surface;
    Left := ScaleX(12);
    Top := ScaleY(258);
    Width := CfgPage.SurfaceWidth - ScaleX(24);
    WordWrap := True;
    Caption := 'Example: 2283:100.101.102.103:2283 (local : IP : port) - leave empty to configure later in Settings.';
    Font.Color := $00808080;
  end;
end;

{ ---- validation ------------------------------------------------ }

function NextButtonClick(CurPageID: Integer): Boolean;
var
  Ip, Line: String;
  i: Integer;
  Parts: TStringList;
begin
  Result := True;
  { silent installs skip the custom pages - no validation, no boxes }
  if WizardSilent then Exit;
  if CurPageID = CfgPage.ID then
  begin
    Ip := Trim(IpEdit.Text);
    if Ip = '' then
    begin
      MsgBox('Please enter the tailnet IP of the machine running your services' + #13#10 +
             '(find it with:  tailscale ip -4  on that machine).', mbError, MB_OK);
      Result := False;
      Exit;
    end;
    { bare IP or hostname only - no port, no spaces }
    if (Pos(':', Ip) > 0) or (Pos(' ', Ip) > 0) or (Pos('.', Ip) <= 0) then
    begin
      MsgBox('Enter the tailnet IP only - e.g. 100.101.102.103 (run "tailscale ip -4"' + #13#10 +
             'on the machine hosting your services). No port, no host:port.', mbError, MB_OK);
      Result := False;
      Exit;
    end;

    { guard: entering this PC's own tailnet IP is a classic mistake }
    if (OwnIp <> '') and (Ip = OwnIp) then
    begin
      if MsgBox('That is THIS computer''s own tailnet IP (' + OwnIp + ').' + #13#10 +
                'Your services usually run on a different machine. Continue anyway?',
                mbConfirmation, MB_YESNO) = IDNO then
      begin
        Result := False;
        Exit;
      end;
    end;
    Parts := TStringList.Create;
    try
      { the forward format is local:host:port - split on ':' explicitly
        (DelimitedText defaults to comma-separated!) }
      Parts.Delimiter := ':';
      Parts.StrictDelimiter := True;
      for i := 0 to ForwardsMemo.Lines.Count - 1 do
      begin
        Line := Trim(ForwardsMemo.Lines[i]);
        if Line = '' then Continue;
        StringChangeEx(Line, '<tailnet-ip>', Ip, True);
        Parts.DelimitedText := Line;
        if (Parts.Count <> 3) or (StrToIntDef(Parts[0], -1) <= 0) or
           (StrToIntDef(Parts[2], -1) <= 0) then
        begin
          MsgBox('Bad forward line: ' + Line + #13#10 +
                 'Use: local:tailnet-ip:port   (e.g. 2283:100.101.102.103:2283)', mbError, MB_OK);
          Result := False;
          Exit;
        end;
      end;
    finally
      Parts.Free;
    end;
    if Result and (Trim(DistroEdit.Text) = '') then
    begin
      MsgBox('The WSL distro name cannot be empty.', mbError, MB_OK);
      Result := False;
    end;
  end;
end;

{ ---- write tailport.config after install ---------------------- }

procedure CurStepChanged(CurStep: TSetupStep);
var
  ConfigPath, Text, Ip, Distro, Line, Py: String;
  i, N: Integer;
begin
  if CurStep = ssPostInstall then
  begin
    ConfigPath := ExpandConstant('{app}\tailport.config');
    { never clobber an existing config (upgrade/reinstall keeps settings) }
    if FileExists(ConfigPath) then Exit;

    { silent installs never fill the wizard fields: hand over the example
      template instead of writing a config with empty values }
    if Trim(IpEdit.Text) = '' then
    begin
      CopyFile(ExpandConstant('{app}\tailport.config.example'), ConfigPath, False);
      Exit;
    end;

    Ip := Trim(IpEdit.Text);
    Distro := Trim(DistroEdit.Text);
    if Distro = '' then Distro := WSL_DISTRO;
    Py := Trim(DetectedPythonw);

    Text := '# ============================================================' + #13#10 +
            '#  Tailport configuration - created by the Tailport installer.' + #13#10 +
            '#  Tailport is the Astrill-safe door to your Tailscale tailnet:' + #13#10 +
            '#  every service listed here answers on http://localhost:<port>.' + #13#10 +
            '# ============================================================' + #13#10 + #13#10 +
            '# Python: full path to pythonw.exe (empty = use system PATH)' + #13#10 +
            'pythonw=' + Py + #13#10 + #13#10 +
            '# WSL2: the distro that runs tailscaled' + #13#10 +
            'wsl_distro=' + Distro + #13#10 + #13#10 +
            '# tailnet door (usually leave as-is)' + #13#10 +
            'socks_host=127.0.0.1' + #13#10 +
            'socks_port=1055' + #13#10 + #13#10 +
            '# port forwards: local:tailnet-ip:port (one list)' + #13#10;

    N := 0;
    for i := 0 to ForwardsMemo.Lines.Count - 1 do
    begin
      Line := Trim(ForwardsMemo.Lines[i]);
      if Line = '' then Continue;
      StringChangeEx(Line, '<tailnet-ip>', Ip, True);
      N := N + 1;
      Text := Text + 'forward.' + IntToStr(N) + '=' + Line + #13#10;
    end;
    Text := Text + #13#10;

    SaveStringToFile(ConfigPath, Text, False);
  end;
end;
