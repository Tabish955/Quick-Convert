# Phase 5 Specification: Shell Integration and Packaging

**Status:** approved and in progress  
**Scope:** Phase 5 only. Windows Explorer context menu integration, per-user Inno Setup installer, startup tool probe, and SmartScreen/signing documentation.  
**Prerequisites:** Phase 1 through Phase 4 merged into `main` (commit `e99ffe7`).

---

## 1. Objective

Phase 5 delivers end-to-end OS integration and consumer-ready installation for Quick-Convert without requiring administrator elevation at any point.

1. **Shell Integration (`QuickConvert.Core.Shell`):**
   - Per-user classic Windows Explorer context menu registration under `HKCU\Software\Classes\SystemFileAssociations\<ext>\shell\QuickConvert`.
   - Multi-selection model configured to `Player` (supporting up to 100 items), routing each selected file as `"%1"` into the running single-instance pipe service with 150 ms debounce.
   - State tracking via `%LOCALAPPDATA%\QuickConvert\shell-registration.json` storing the exact list of registered extensions and product version.
   - Idempotent registration and complete, residue-free unregistration.
   - Registry abstraction (`IRegistry`) enabling unit test verification without real registry mutation.
   - Headless CLI switches on `QuickConvert.UI.exe`: `--register-shell`, `--unregister-shell`, `--version`.
2. **Per-User Installer (`installer/QuickConvert.iss`):**
   - Inno Setup 6 script configured with `PrivilegesRequired=lowest`.
   - Installation into `{localappdata}\Programs\QuickConvert`.
   - Zero UAC/elevation prompt anywhere in the installation or uninstallation lifecycle.
   - Bundles all external CLI tools (`tools\ffmpeg`, `tools\pandoc`, `tools\wkhtmltopdf`, `tools\7z`).
   - Automated registration/unregistration hook executing `QuickConvert.UI.exe --register-shell` and `QuickConvert.UI.exe --unregister-shell`.
   - Clear policy on logs and configuration preservation upon uninstall.
3. **First-Run Tool Probe:**
   - Asynchronous, non-blocking startup check for all four tools (`ffmpeg`, `pandoc`, `wkhtmltopdf`, `7za`).
   - If any tool is missing, displays an informative dialog naming the tool and its developer installation script (`tools\Get-*.ps1`).
   - Degrades gracefully to available engines without crashing or prompting for downloads.
4. **SmartScreen & Code Signing (Decision D4):**
   - Formal resolution of D4 for v1.0: unsigned installer and binaries with documented SmartScreen bypass procedure.
   - Dedicated documentation in `docs/SMARTSCREEN.md`.

---

## 2. Shell Integration Architecture

### 2.1 Registry Schema
For each extension `ext` in `ConversionMatrix.Sources` (34 total source extensions):

```
HKCU\Software\Classes\SystemFileAssociations\<ext>\shell\QuickConvert
    (Default)          = "Convert with QuickConvert"
    Icon               = "<InstallDirectory>\QuickConvert.UI.exe"
    MultiSelectModel   = "Player"

HKCU\Software\Classes\SystemFileAssociations\<ext>\shell\QuickConvert\command
    (Default)          = "\"<InstallDirectory>\QuickConvert.UI.exe\" \"%1\""
```

### 2.2 Selection Model & Multi-Launch Debounce
- Windows Explorer enforces selection limits based on `MultiSelectModel`: `Document` limits verbs to 15 selected items, whereas `Player` extends the limit to 100 items.
- Explorer executes one launcher process per selected file passing `"%1"`.
- Each secondary instance transmits its single argument to the primary instance over Named Pipe `QuickConvert_Explorer_Pipe` and terminates.
- The primary instance's 150 ms debounce timer consolidates all incoming transmissions into a single batch, launching the Quick Popup once with all files populated.

### 2.3 Registration State Tracking
To prevent orphaned registry entries if the format matrix changes between versions, registration writes a manifest to:
`%LOCALAPPDATA%\QuickConvert\shell-registration.json`

```json
{
  "Version": "1.0.0",
  "ExecutablePath": "C:\\Users\\...\\AppData\\Local\\Programs\\QuickConvert\\QuickConvert.UI.exe",
  "RegisteredExtensions": [
    ".aac", ".avi", ".bmp", ".csv", ".docx", ".flac", ".gif", ".gz",
    ".html", ".jpeg", ".jpg", ".json", ".m4a", ".mkv", ".mov", ".mp3",
    ".mp4", ".odt", ".ogg", ".pdf", ".png", ".rar", ".rtf", ".tar",
    ".tiff", ".txt", ".wav", ".webm", ".webp", ".wma", ".wmv", ".zip", ".7z"
  ],
  "TimestampUtc": "2026-11-16T15:30:00Z"
}
```

Unregistration reads this manifest (falling back to `ConversionMatrix.Sources` if absent) and deletes all associated keys, leaving zero orphaned keys in `HKCU\Software\Classes\SystemFileAssociations`.

### 2.4 Registry Abstraction & Error Handling
```csharp
public interface IRegistryKey : IDisposable
{
    void SetValue(string? name, object value);
    object? GetValue(string? name);
    IRegistryKey CreateSubKey(string subkey);
    IRegistryKey? OpenSubKey(string subkey, bool writable = false);
    void DeleteSubKeyTree(string subkey, bool throwOnMissingSubKey = false);
    string[] GetSubKeyNames();
}

public interface IRegistry
{
    IRegistryKey CurrentUserClasses { get; }
}
```
`ShellIntegrationService` wraps all operations in defensive error handling: registry errors are logged via `QuickConvertLogger.Warn` / `Error` and return `ShellOperationResult.Failed(message)` without throwing into the UI or crashing the host process.

---

## 3. CLI Switches on QuickConvert.UI.exe

When launched with headless management switches, `App.xaml.cs` executes the command synchronously and exits with a return code without initializing the WPF UI or Named Pipe server:

| Switch | Exit Code | Action |
|---|---|---|
| `--register-shell` | 0 on success, 1 on error | Registers verbs for all matrix extensions and writes `shell-registration.json`. |
| `--unregister-shell` | 0 on success, 1 on error | Unregisters all previously recorded extensions and removes `shell-registration.json`. |
| `--version` | 0 | Prints `QuickConvert v1.0.0` and exits. |

---

## 4. Inno Setup Packaging Architecture

### 4.1 Configuration Details (`installer/QuickConvert.iss`)
- **AppId:** `{{7B95F56E-8041-4E6C-962E-0C4C7E835A12}}`
- **PrivilegesRequired:** `lowest` (strictly non-administrative, per-user).
- **DefaultDirName:** `{localappdata}\Programs\QuickConvert`.
- **Architecture:** x64 only (`ArchitecturesAllowed=x64compatible`, `ArchitecturesInstallIn64BitMode=x64compatible`).
- **Compression:** `lzma2/ultra64`, `SolidCompression=yes`.
- **Tasks:**
  - `desktopicon`: Optional desktop shortcut.
  - `contextmenu`: Context menu integration (checked by default).
- **Run Section (Install):**
  - Executes `{app}\QuickConvert.UI.exe --register-shell` (if `contextmenu` task selected).
- **UninstallRun Section (Uninstall):**
  - Executes `{app}\QuickConvert.UI.exe --unregister-shell` with `RunOnceId` before file removal.

### 4.2 Application Publish Model
- Framework-dependent publish targeting `.NET 9 Desktop Runtime` (`win-x64`).
- Documented tradeoff: self-contained adds ~60 MB to the payload, whereas framework-dependent keeps the application binaries minimal (~15 MB) and bundles the required tools.
- ReadyToRun and single-file compilation are deferred to Phase 6 (where cold-start optimization to < 500 ms is targeted).

### 4.3 Retention Policy on Uninstall
- Executable, bundled tools, and shortcuts are deleted cleanly.
- Registry entries under `HKCU\Software\Classes\SystemFileAssociations` and `HKCU\Software\Microsoft\Windows\CurrentVersion\Uninstall` are purged 100%.
- Application logs (`%LOCALAPPDATA%\QuickConvert\logs`) are preserved intentionally per Windows diagnostic conventions, allowing troubleshooting of recent sessions.

---

## 5. First-Run Tool Probe

Upon interactive startup (`OnStartup` without headless flags):
1. Background task asynchronously queries `ToolLocator` / `FfmpegLocator` for `ffmpeg.exe`, `pandoc.exe`, `wkhtmltopdf.exe`, and `7za.exe`.
2. Probes run without blocking WPF window initialization.
3. If any tool is missing, a unified notification dialog displays:
   - Specific tool names missing.
   - Corresponding setup script (`tools\Get-Ffmpeg.ps1`, `tools\Get-Pandoc.ps1`, etc.).
   - Reassurance that Quick-Convert will continue operating with reduced capability for formats that do not require the missing tools.
4. No network requests are made.

---

## 6. Decision Gates Addressed

- **D2 (Context Menu):** Resolved for v1.0 as classic HKCU verb (`SystemFileAssociations`). Windows 11 placement behind "Show more options" is accepted for v1.0; modern sparse MSIX package deferred to v1.1.
- **D4 (Code Signing):** Resolved for v1.0 as unsigned installer with documented SmartScreen walkthrough in `docs/SMARTSCREEN.md`.
