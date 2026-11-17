; =============================================================================
; QuickConvert — Inno Setup 6 Script
; Per-user installation (no administrator privileges required)
; =============================================================================

#define MyAppName "QuickConvert"
#define MyAppVersion "1.0.0"
#define MyAppPublisher "QuickConvert Team"
#define MyAppURL "https://github.com/Tabish955/Quick-Convert"
#define MyAppExeName "QuickConvert.UI.exe"

[Setup]
; Unique GUID for per-user installation
AppId={{7B95F56E-8041-4E6C-962E-0C4C7E835A12}}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
AppUpdatesURL={#MyAppURL}

; Strictly per-user: zero elevation, no UAC prompts
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
DefaultDirName={localappdata}\Programs\{#MyAppName}
DisableProgramGroupPage=yes
DefaultGroupName={#MyAppName}

; Output configuration
OutputDir=..\artifacts\installer
OutputBaseFilename=QuickConvert-Setup-{#MyAppVersion}
SetupIconFile=..\src\QuickConvert.UI\QuickConvert.ico
UninstallDisplayIcon={app}\{#MyAppExeName}

; High-efficiency solid compression (LZMA2 ultra)
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern

; 64-bit Windows requirement
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "contextmenu"; Description: "Add ""Convert with QuickConvert"" to File Explorer context menu"; Flags: checkedonce
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
; Main application binaries (published framework-dependent)
Source: "..\publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

; Bundled CLI Tools
Source: "..\tools\ffmpeg\bin\*"; DestDir: "{app}\tools\ffmpeg\bin"; Flags: ignoreversion recursesubdirs
Source: "..\tools\pandoc\pandoc.exe"; DestDir: "{app}\tools\pandoc"; Flags: ignoreversion
Source: "..\tools\wkhtmltopdf\*"; DestDir: "{app}\tools\wkhtmltopdf"; Flags: ignoreversion recursesubdirs
Source: "..\tools\7z\7za.exe"; DestDir: "{app}\tools\7z"; Flags: ignoreversion

; Notices and licensing
Source: "..\LICENSE"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\THIRD-PARTY-NOTICES.md"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; IconFilename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; IconFilename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
; Register shell context menu verbs if selected
Filename: "{app}\{#MyAppExeName}"; Parameters: "--register-shell"; Flags: runhidden; Tasks: contextmenu
; Launch application option
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent

[UninstallRun]
; Cleanly remove shell verbs before removing application files
Filename: "{app}\{#MyAppExeName}"; Parameters: "--unregister-shell"; Flags: runhidden; RunOnceId: "QuickConvertUnregisterShell"

[UninstallDelete]
; Delete the manifest, the rolling logs and any now-empty state folder
Type: files; Name: "{localappdata}\QuickConvert\shell-registration.json"
Type: filesandordirs; Name: "{localappdata}\QuickConvert\logs"
Type: dirifempty; Name: "{localappdata}\QuickConvert"

[Code]
; The application is published framework dependent, so the .NET 9 Desktop Runtime has to be present.
; The presence of the shared framework registry key proves only that some version exists, so the host
; is asked directly and its output is searched for the 9.x line.
function DotNetExePath(): String;
var
  Candidate: String;
begin
  Result := '';
  Candidate := ExpandConstant('{commonpf64}\dotnet\dotnet.exe');
  if FileExists(Candidate) then begin Result := Candidate; Exit; end;
  Candidate := ExpandConstant('{commonpf}\dotnet\dotnet.exe');
  if FileExists(Candidate) then begin Result := Candidate; Exit; end;
  Candidate := ExpandConstant('{localappdata}\Microsoft\dotnet\dotnet.exe');
  if FileExists(Candidate) then begin Result := Candidate; Exit; end;
end;

function HasDotNet9DesktopRuntime(): Boolean;
var
  DotnetPath, OutputFile: String;
  ResultCode, I: Integer;
  Lines: TArrayOfString;
begin
  Result := False;
  DotnetPath := DotNetExePath();
  if DotnetPath = '' then Exit;

  OutputFile := ExpandConstant('{tmp}\dotnet-runtimes.txt');
  DeleteFile(OutputFile);

  if not Exec(ExpandConstant('{cmd}'),
              '/c ""' + DotnetPath + '" --list-runtimes > "' + OutputFile + '" 2>nul"',
              '', SW_HIDE, ewWaitUntilTerminated, ResultCode) then
    Exit;

  if not LoadStringsFromFile(OutputFile, Lines) then Exit;

  for I := 0 to GetArrayLength(Lines) - 1 do
  begin
    if Pos('Microsoft.WindowsDesktop.App 9.', Lines[I]) > 0 then
    begin
      Result := True;
      Exit;
    end;
  end;
end;

function InitializeSetup(): Boolean;
var
  Answer: Integer;
begin
  Result := True;
  if HasDotNet9DesktopRuntime() then Exit;

  Answer := MsgBox(
    'QuickConvert needs the .NET 9 Desktop Runtime (x64) to run.' + #13#10 + #13#10 +
    'Windows does not appear to have version 9 of that runtime installed, and this setup cannot run ' +
    'the application without it.' + #13#10 + #13#10 +
    'Install it from https://dotnet.microsoft.com/download/dotnet/9.0 and start this setup again,' +
    #13#10 + 'or continue if you intend to install the runtime later.' + #13#10 + #13#10 +
    'Continue the installation anyway?',
    mbConfirmation, MB_YESNO or MB_DEFBUTTON2);
  Result := (Answer = IDYES);
end;
