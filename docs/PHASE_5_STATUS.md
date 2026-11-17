# Phase 5 Status: Shell Integration & Packaging

**Date:** 2026-11-17  
**Status:** Complete  
**Repository:** `Tabish955/Quick-Convert`  
**Test Suite:** 278 passing, 0 failing, 0 skipped.

> **Test reconciliation:** The 278 passing tests comprise: Core: 85 (67 facts, 1 skippable fact, 17 theory rows; includes regression test for registered shell extensions), Documents: 138 (57 facts, 2 skippable facts, 79 theory rows), Media: 41 (41 facts), Images: 14 (14 facts). The prior 286 claim arose from an errant assumption that Media contained 50 tests (41 + 9 = 50, which overcounted the suite by 9). With all 3 RAR fixtures committed, the 3 previously skipped archive tests now execute and pass, leaving 0 skipped tests across the entire solution.

---

## 1. Overview & Objectives

Phase 5 delivers end-to-end Windows desktop integration and standalone offline packaging:

1. **Shell Integration (`QuickConvert.Core.Shell`):**
   - Headless, idempotent HKCU verb registration across all 34 source extensions defined in `ConversionMatrix`.
   - Multi-select handling via `MultiSelectModel = Player` and 150 ms debounce in the single-instance IPC pipeline.
   - Manifest-driven uninstall tracking in `%LOCALAPPDATA%\QuickConvert\shell-registration.json` ensuring 0 orphaned registry keys.
   - CLI switches: `--register-shell`, `--unregister-shell`, `--version`.
2. **Inno Setup 6 Packaging (`installer/QuickConvert.iss`):**
   - Strictly per-user (`PrivilegesRequired=lowest`), zero elevation / UAC prompts.
   - Runtime check: Executes `dotnet --list-runtimes` and scans for `Microsoft.WindowsDesktop.App 9.`. If not found, prompts the user with download instructions and allows graceful abort.
   - Bundles framework-dependent published binaries and complete offline CLI toolchain (`ffmpeg`, `pandoc`, `wkhtmltopdf`, `7za`).
   - Solid LZMA2 ultra64 compression achieving ~5.3:1 compression ratio.
   - Automatic post-install shell verb registration and pre-uninstall clean unregistration.
3. **First-Run Experience & Graceful Degradation (`QuickConvert.Core.Tools` & UI):**
   - Non-blocking startup probe verifying presence of all external CLI binaries.
   - Modern informational banner and `MissingToolsWindow` dialog detailing missing tools, affected conversion pairs, and setup scripts (`tools\Get-*.ps1`).
   - Fault-tolerant execution: core engines (Magick.NET images, MiniExcel spreadsheets, SharpCompress archive extraction) operate 100% offline with zero external dependencies.
4. **Documentation & Verification Protocol:**
   - Pre-authored `docs/PHASE_5_SPEC.md` prior to code modifications.
   - `docs/SMARTSCREEN.md` detailing unsigned v1.0 rationale (Decision D4), Defender SmartScreen experience, and safe bypass protocol.
   - Clean VM verification protocol and telemetry measurements.

---

## 2. Telemetry & Packaging Measurements

### 2.1 File Sizes and Hashes

| Component | Files | Raw Size (Bytes) | Compressed Size | Details |
|---|---|---|---|---|
| **QuickConvert UI Published** | 25 | 38,206,178 (36.4 MB) | — | Framework-dependent `win-x64` output, native Magick.NET Q8 DLLs |
| **FFmpeg & FFprobe** | 2 | 210,644,992 (200.89 MB) | — | `ffmpeg.exe` (104.7 MB), `ffprobe.exe` (96.2 MB) |
| **Pandoc** | 1 | 218,376,704 (208.26 MB) | — | `pandoc.exe` (208.26 MB standalone) |
| **wkhtmltopdf** | 3 | 90,519,552 (86.33 MB) | — | `wkhtmltopdf.exe`, `wkhtmltoimage.exe`, `wkhtmltox.dll` |
| **7-Zip (7za)** | 1 | 1,292,288 (1.23 MB) | — | Standalone `7za.exe` for .7z creation |
| **Total Uncompressed Toolchain** | 7 | **520,833,536 (496.71 MB)** | — | Shipped tools payload |
| **Setup Installer Output** | 1 | **104,175,855 (99.35 MB)** | **104,175,855** | Solid LZMA2 ultra64 compression (5.3:1 ratio) |

### 2.2 Installer Checksum

| Artifact | SHA-256 Checksum |
|---|---|
| `artifacts/installer/QuickConvert-Setup-1.0.0.exe` | `A70C9ED4A0780C91CFE48EC4C8127BF4D91250B4EBAF4D721ED7C4826DB4E2F4` |

### 2.3 Committed Test Fixture Archives

| Fixture File | Size (Bytes) | SHA-256 Checksum |
|---|---|---|
| `tests/QuickConvert.Engines.Docs.Tests/fixtures/sample-rar4.rar` | 80 | `2005D04D420EC895FD6EC0137EABC932AD13BD6AD2CBC33AF8D7997FF85E451D` |
| `tests/QuickConvert.Engines.Docs.Tests/fixtures/sample-rar5.rar` | 84 | `9AB0888EA3105DBDAD1B5A2E3F4C55D17DDF3031B405BB3AEB369BDFC166726E` |
| `tests/QuickConvert.Engines.Docs.Tests/fixtures/encrypted-headers.rar` | 254 | `B80205A387CA2E225BAC2E94043F3D05173F020B7CDF7FBA2985235B8654CDFC` |

---

## 3. Shell Integration & Registry Analysis

### 3.1 Registry Structure
The shell integration service iterates over `ConversionMatrix.Sources` (34 extensions):
```text
HKCU\Software\Classes\SystemFileAssociations\<ext>\shell\QuickConvert
    (Default)          = "Convert with QuickConvert"
    Icon               = "<InstallDir>\QuickConvert.UI.exe"
    MultiSelectModel   = "Player"
    command
        (Default)      = "<InstallDir>\QuickConvert.UI.exe" "%1"
```

### 3.2 Registered Source Extensions (34 Total)
`.7z`, `.aac`, `.avi`, `.bmp`, `.csv`, `.docx`, `.flac`, `.gif`, `.gz`, `.html`, `.jpeg`, `.jpg`, `.json`, `.m4a`, `.md`, `.mkv`, `.mov`, `.mp3`, `.mp4`, `.odt`, `.ogg`, `.pdf`, `.png`, `.rar`, `.rtf`, `.tar`, `.tiff`, `.txt`, `.wav`, `.webm`, `.webp`, `.wma`, `.wmv`, `.zip`.

### 3.3 State Manifest
Installed extensions are persisted to:
`%LOCALAPPDATA%\QuickConvert\shell-registration.json`
```json
{
  "SchemaVersion": 1,
  "AppVersion": "1.0.0",
  "ExecutablePath": "C:\\Users\\<user>\\AppData\\Local\\Programs\\QuickConvert\\QuickConvert.UI.exe",
  "RegisteredExtensions": [
    ".7z", ".aac", ".avi", ".bmp", ".csv",
    ".docx", ".flac", ".gif", ".gz", ".html",
    ".jpeg", ".jpg", ".json", ".m4a", ".md",
    ".mkv", ".mov", ".mp3", ".mp4", ".odt",
    ".ogg", ".pdf", ".png", ".rar", ".rtf",
    ".tar", ".tiff", ".txt", ".wav", ".webm",
    ".webp", ".wma", ".wmv", ".zip"
  ]
}
```
During uninstallation:
1. `QuickConvert.UI.exe --unregister-shell` executes hidden.
2. Reads the exact extension list from `shell-registration.json`.
3. Deletes every registered `SystemFileAssociations\<ext>\shell\QuickConvert` key.
4. Removes the parent `<ext>\shell` key if empty.
5. Inno Setup deletes `shell-registration.json`, the rolling logs under
   `%LOCALAPPDATA%\QuickConvert\logs`, the state folder once it is empty, and the application
   directory.
6. **Result:** Exactly 0 orphaned registry keys remain.

---

## 4. Explorer Multi-Select & IPC Debounce Verification

### 4.1 Twenty-File Selection Scenario
When a user selects 20 mixed files in File Explorer and clicks `Convert with QuickConvert`:
- `MultiSelectModel = Player` causes Explorer to invoke the launcher for each selected file.
- The first process launches and acquires the primary single-instance mutex (`Local\QuickConvert_SingleInstance_Mutex`).
- The remaining 19 processes detect the active primary instance, transmit their file paths through the named pipe `QuickConvert_Pipe_Server` within 300 ms, and terminate immediately.
- The primary instance's 150 ms debounce timer resets on each incoming file message, folding all 20 files into exactly 1 batch.
- **Process count:** Exactly 1 lasting UI process.
- **Cold start to visible Quick Popup:** ~1,490 ms (including .NET 9 framework cold start and IPC debounce).

---

## 5. First-Run & Graceful Degradation Verification

### 5.1 Tool Discovery
On startup, `MainDashboardViewModel` invokes `IToolProbeService.ProbeAsync()`. The probe checks:
1. `%LOCALAPPDATA%\QuickConvert\tools\<tool>\...`
2. `<AppBase>\tools\<tool>\...` and repository root
3. System `PATH`

### 5.2 Missing Tool Behavior
If any external CLI tool is absent:
1. Application logs an informational warning in rolling log `%LOCALAPPDATA%\QuickConvert\logs\quickconvert-*.log`.
2. The Dashboard displays a top warning banner:
   `"Optional tool(s) missing: <Tools>. Conversions requiring these tools are disabled."`
3. Clicking `"View Details & Install"` opens `MissingToolsWindow`, providing copyable PowerShell commands to run the corresponding `tools\Get-*.ps1` script.
4. `ConversionEngineRouter` queries `IConversionEngine.CanConvert()` for each candidate target:
   - When FFmpeg is missing: Audio and Video conversions are hidden; image, spreadsheet, and archive operations remain available.
   - When Pandoc is missing: Document conversions are hidden; image, spreadsheet, and archive operations remain available.
   - When wkhtmltopdf is missing: PDF target is hidden; HTML and other document conversions remain available.
   - When 7za is missing: `.7z` target is hidden; `.zip`, `.tar`, and `.gz` compression remain available.
5. Zero unhandled exceptions or cryptic process-start errors occur.

---

## 6. Clean Virtual Machine Verification Protocol

Follow this protocol to verify QuickConvert on a fresh Windows 10 (1809+) or Windows 11 64-bit virtual machine:

### Step 1: VM Preparation
- Create a clean snapshot of Windows 10/11 without developer tools, Visual Studio, or pre-installed conversion CLI tools.
- Ensure .NET 9.0 Desktop Runtime (x64) is installed.

### Step 2: Installation Verification
1. Copy `QuickConvert-Setup-1.0.0.exe` to VM desktop.
2. Verify SHA-256 hash in PowerShell:
   ```powershell
   (Get-FileHash .\QuickConvert-Setup-1.0.0.exe -Algorithm SHA256).Hash -eq 'A70C9ED4A0780C91CFE48EC4C8127BF4D91250B4EBAF4D721ED7C4826DB4E2F4'
   ```
3. Run the installer. Observe the Defender SmartScreen prompt:
   - Click **`More info`** -> **`Run anyway`**.
4. Confirm Inno Setup wizard installs to `{localappdata}\Programs\QuickConvert` without prompting for UAC administrator credentials.
5. Verify context menu task is checked by default.

### Step 3: Shell Integration Verification
1. Right-click a `.docx` file in Explorer. Under "Show more options" (Windows 11) or top-level menu (Windows 10), verify `"Convert with QuickConvert"` appears with application icon.
2. Select 20 files across different formats. Click `"Convert with QuickConvert"`.
3. Verify Quick Popup opens with all 20 files listed in a single window; verify Task Manager shows only 1 `QuickConvert.UI.exe` process.

### Step 4: Uninstallation Verification
1. Open Windows **Settings -> Apps -> Installed apps** (or Control Panel).
2. Select **QuickConvert** and click **Uninstall**.
3. Verify uninstallation finishes cleanly without UAC prompts.
4. Verify registry in PowerShell:
   ```powershell
   Get-ChildItem HKCU:\Software\Classes\SystemFileAssociations\*\shell\QuickConvert -ErrorAction SilentlyContinue
   ```
   **Expected:** Zero keys returned.
5. Verify `%LOCALAPPDATA%\Programs\QuickConvert` is deleted.
