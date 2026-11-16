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
; Delete manifest and clean installation directory residue
Type: files; Name: "{localappdata}\QuickConvert\shell-registration.json"

[Code]
// Helper function to detect whether .NET 9.0 Desktop Runtime is present
function IsDotNet9Installed(): Boolean;
var
  ResultCode: Integer;
begin
  Result := False;
  // Check shared runtime registration in HKLM or via dotnet command
  if RegKeyExists(HKLM, 'SOFTWARE\dotnet\Setup\InstalledVersions\x64\sharedfx\Microsoft.WindowsDesktop.App') then
  begin
    Result := True;
    Exit;
  end;
  if RegKeyExists(HKCU, 'SOFTWARE\dotnet\Setup\InstalledVersions\x64\sharedfx\Microsoft.WindowsDesktop.App') then
  begin
    Result := True;
    Exit;
  end;
  // Fallback: check dotnet CLI
  if Exec('dotnet.exe', '--list-runtimes', '', SW_HIDE, ewWaitUntilTerminated, ResultCode) then
  begin
    Result := (ResultCode = 0);
  end;
end;

function InitializeSetup(): Boolean;
begin
  Result := True;
  if not IsDotNet9Installed() then
  begin
    MsgBox(
      'QuickConvert requires the .NET 9.0 Desktop Runtime (x64) to run.' + #13#10 + #13#10 +
      'If you encounter an error when launching, please install .NET 9.0 Desktop Runtime from:' + #13#10 +
      'https://dotnet.microsoft.com/download/dotnet/9.0',
      mbInformation, MB_OK);
  end;
end;
