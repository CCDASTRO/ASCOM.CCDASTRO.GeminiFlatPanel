; Based on the ASCOM Driver Installer Generator's DriverInstallTemplate.iss.
; .NET local-server variant: the executable registers both served interfaces.
#define DriverExe "ASCOM.CCDASTRO.GeminiFlatPanel.exe"
#define BinaryDir "..\src\GeminiFlatPanel.Server\bin\Release\net48"
#define DriverVersion GetVersionNumbersString(BinaryDir + "\" + DriverExe)

[Setup]
AppId={{cba8b5f9-936e-4f3a-8d26-a776ecba9ecf}
AppName=CCDASTRO Gemini FlatPanel ASCOM Drivers
AppVersion={#DriverVersion}
AppVerName=CCDASTRO Gemini FlatPanel {#DriverVersion}
AppPublisher=CCDASTRO
VersionInfoVersion={#DriverVersion}
MinVersion=10.0
PrivilegesRequired=admin
DefaultDirName={commoncf32}\ASCOM\CoverCalibrator\CCDASTRO.GeminiFlatPanel
DisableDirPage=yes
DefaultGroupName=CCDASTRO Gemini FlatPanel
DisableProgramGroupPage=yes
UninstallFilesDir={commoncf32}\ASCOM\Uninstall\CoverCalibrator\CCDASTRO.GeminiFlatPanel
OutputDir=..\dist
OutputBaseFilename=CCDASTRO.GeminiFlatPanel.Setup-{#DriverVersion}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
WizardImageFile=resources\WizardImage.bmp
CloseApplications=no
RestartApplications=no
UninstallDisplayIcon={app}\{#DriverExe}

[Languages]
Name: english; MessagesFile: compiler:Default.isl

[Files]
Source: "{#BinaryDir}\{#DriverExe}"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#BinaryDir}\{#DriverExe}.config"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#BinaryDir}\GeminiFlatPanel.Core.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\docs\GeminiFlatPanel-Guide.html"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\Driver and Switch guide"; Filename: "{app}\GeminiFlatPanel-Guide.html"

[Run]
Filename: "{app}\{#DriverExe}"; Parameters: "/register"; Flags: runhidden waituntilterminated; AfterInstall: VerifyRegistration
Filename: "{app}\GeminiFlatPanel-Guide.html"; Description: "Open the driver and Switch usage guide"; Flags: postinstall shellexec skipifsilent unchecked runasoriginaluser

[UninstallRun]
Filename: "{app}\{#DriverExe}"; Parameters: "/unregister"; Flags: runhidden waituntilterminated; RunOnceId: UnregisterGemini

[Code]
// ASCOM template prerequisite check, using integer version parsing to avoid
// locale-dependent decimal separators. Require the tested Platform 7.1 baseline.
function PlatformReady(): Boolean;
var V: String; Dot: Integer; Major, Minor: Integer;
begin
  Result := False;
  if not RegQueryStringValue(HKLM32, 'Software\ASCOM', 'PlatformVersion', V) then Exit;
  Dot := Pos('.', V);
  if Dot = 0 then Exit;
  Major := StrToIntDef(Copy(V, 1, Dot - 1), 0);
  V := Copy(V, Dot + 1, Length(V));
  Dot := Pos('.', V);
  if Dot > 0 then V := Copy(V, 1, Dot - 1);
  Minor := StrToIntDef(V, 0);
  Result := (Major > 7) or ((Major = 7) and (Minor >= 1));
end;

function GeminiRunning(): Boolean;
var Locator, Service, Processes: Variant;
begin
  Result := True;
  try
    Locator := CreateOleObject('WbemScripting.SWbemLocator');
    Service := Locator.ConnectServer('.', 'root\CIMV2');
    Processes := Service.ExecQuery('SELECT ProcessId FROM Win32_Process WHERE Name="{#DriverExe}"');
    Result := Processes.Count > 0;
  except
    Log('Could not verify that the Gemini server is closed.');
  end;
end;

function InitializeSetup(): Boolean;
var Release: Cardinal;
begin
  Result := False;
  if not PlatformReady() then begin
    MsgBox('Install ASCOM Platform 7.1 or later from https://ascom-standards.org before installing this driver.', mbCriticalError, MB_OK);
    Exit;
  end;
  if not RegQueryDWordValue(HKLM32, 'SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full', 'Release', Release) then Release := 0;
  if Release < 528040 then begin
    MsgBox('Microsoft .NET Framework 4.8 or later is required.', mbCriticalError, MB_OK);
    Exit;
  end;
  Result := True;
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  Result := '';
  if GeminiRunning() then Result := 'Disconnect both Gemini devices in all ASCOM clients and close Gemini setup, then retry. Setup will not stop a running driver.';
end;

function InitializeUninstall(): Boolean;
begin
  Result := not GeminiRunning();
  if not Result then MsgBox('Disconnect both Gemini devices and close Gemini setup before uninstalling.', mbCriticalError, MB_OK);
end;

procedure CheckRegistration(Root: Integer; Clsid: String);
var ServerPath: String;
begin
  if not RegQueryStringValue(Root, 'SOFTWARE\Classes\CLSID\' + Clsid + '\LocalServer32', '', ServerPath) then
    RaiseException('Gemini COM registration is missing.');
  if Pos(Lowercase(ExpandConstant('{app}\{#DriverExe}')), Lowercase(ServerPath)) = 0 then
    RaiseException('Gemini COM registration does not point to the installed driver.');
end;

procedure VerifyRegistration();
begin
  CheckRegistration(HKLM32, '{06F25190-B598-42D5-8207-36754DCD8C2B}');
  CheckRegistration(HKLM32, '{328C89C6-68E1-4A30-B696-57E00E05F975}');
  if IsWin64 then begin
    CheckRegistration(HKLM64, '{06F25190-B598-42D5-8207-36754DCD8C2B}');
    CheckRegistration(HKLM64, '{328C89C6-68E1-4A30-B696-57E00E05F975}');
  end;
end;

