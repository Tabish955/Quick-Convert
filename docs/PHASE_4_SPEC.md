# Phase 4 Specification: Documents, Spreadsheets, and Archives

**Status:** approved and implemented  
**Scope:** Phase 4 only. Architecture, technical specifications, and security constraints for Document, Spreadsheet, and Archive engines.  
**Prerequisites:** Phase 1, Phase 2, and Phase 3 merged into `main` (through commit `c8f8474`).

---

## 1. Objective

Phase 2 and Phase 3 established image and media pipelines, capability routing, subprocess isolation, and atomic promotions. Phase 4 extends Quick-Convert to handle all remaining roadmap formats across three distinct domains:

1. **Document Conversion Engine (`PandocConversionEngine`):**
   - Pandoc 3.5 orchestrator for Markdown, HTML5, DOCX, RTF, ODT, and TXT.
   - Secure PDF generation pipeline via intermediate standalone HTML AST synthesis with embedded resources and `wkhtmltopdf` 0.12.6-1.
   - Resource path resolution for relative images and automatic Markdown image extraction into collision-safe `_media` folders.
2. **Spreadsheet Conversion Engine (`SpreadsheetConversionEngine`):**
   - High-performance, 100% in-process transformation between CSV, JSON, XLSX, TXT, and Markdown tables.
   - Custom allocation-free RFC 4180 CSV parser supporting RFC quote escapes, multi-line values, and CRLF/LF normalization.
   - `MiniExcel` 1.41.0 reader/writer avoiding heavy OpenXML or non-free font dependencies.
3. **Archive Engine (`ArchiveConversionEngine`):**
   - Extraction for ZIP, 7Z, TAR, GZ, and RAR (read-only) via `SharpCompress` 0.40.0.
   - Defense-in-depth security: pre-extraction entry validation against zip-slip directory traversal (`ArchivePathGuard`) and upfront password/encryption classification.
   - In-process compression for ZIP, TAR, and GZ using `SharpCompress`; `.7z` creation delegated to isolated `7za.exe` 23.01 subprocess.
   - Directory-to-archive compression and archive extraction to numbered, collision-free directories (`PromotionHelper`).
4. **Core IO & Coordination:**
   - Multi-endpoint job modeling (`JobEndpointKind.File`, `JobEndpointKind.Directory`).
   - Reserved token `#extract` (`ConversionTargets.Extract`) flowing through existing contracts without contaminating `ConversionMatrix`.
   - Dynamic engine concurrency hints via `IConcurrencyAwareEngine`.
   - Explorer shell integration for folder drag-and-drop, "Extract to folder" UI target label, and directory-aware "Reveal in File Explorer".

---

## 2. Core Architecture & Contracts

### 2.1 Endpoint Kinds & Reserved Targets
`ConversionJob` introduces endpoint discrimination:
```csharp
public enum JobEndpointKind
{
    File = 0,
    Directory = 1
}
```
- Standard file conversions operate with `SourceKind = File, TargetKind = File`.
- Archive extraction operates with `SourceKind = File, TargetKind = Directory` using the reserved token `ConversionTargets.Extract` (`"#extract"`).
- Directory compression operates with `SourceKind = Directory, TargetKind = File`.
- `ConversionMatrix` deliberately forbids reserved tokens: the automated test `ReservedTargets_AreNeverInTheMatrix` enforces this invariant.

### 2.2 Capability Routing
`ConversionEngineRouter` resolves formats with strict safety:
- If `ConversionTargets.IsReserved(target)` or `source` is empty/`<dir>`, the router skips `ConversionMatrix` and queries registered engines directly via `IConversionEngine.CanConvert`.
- All standard format pairs are strictly vetted against `ConversionMatrix` before engines are consulted.
- `GetAvailableTargets(string sourceExt)` dynamically injects `#extract` for extractable archives and `[".zip", ".7z", ".tar", ".gz"]` for directory sources.

### 2.3 Concurrency Control (D7 Concurrency Hint)
To prevent heavy CPU/disk thrashing during complex media or document conversions, engines can optionally advertise recommended concurrency limits:
```csharp
public interface IConcurrencyAwareEngine
{
    int RecommendedMaxDegreeOfParallelism { get; }
}
```
`BatchConversionCoordinator` respects this by combining a global semaphore (`Environment.ProcessorCount / 2`) with per-engine throttling (`Math.Max(1, aware.RecommendedMaxDegreeOfParallelism)`).

---

## 3. Subprocess Execution & Security Hardening

### 3.1 Subprocess Isolation (`ToolProcessRunner`)
- Executables are invoked strictly via `ProcessStartInfo.ArgumentList` with `UseShellExecute = false` and `CreateNoWindow = true`.
- Zero shell interpreters (`cmd.exe`, `powershell.exe`) are ever invoked.
- Robust stderr capture retains a rolling window of the last 50 error lines.
- Complete process tree cancellation (`Kill(entireProcessTree: true)`) on cancellation or timeout.

### 3.2 Offline PDF Generation Pipeline
Pandoc lacks native PDF compilation on Windows without a TeX distribution. Quick-Convert implements a 2-stage offline pipeline:
1. `PandocArgumentBuilder` compiles the document into an intermediate standalone HTML5 document (`intermediate.html`) with `--standalone`, `--embed-resources`, and `--metadata pagetitle=...`.
2. `wkhtmltopdf` renders the intermediate HTML into a production PDF with rigorous security flags:
   - `--disable-javascript`: prevents malicious active scripting.
   - `--enable-local-file-access`: permits local images while ignoring remote requests.
   - `--load-error-handling ignore` and `--load-media-error-handling ignore`: prevents aborting or hanging on missing/unreachable network resources.
   - `--encoding utf-8` and `--page-size A4`: deterministic font and page metrics.

### 3.3 Archive Path Security (`ArchivePathGuard`)
Every entry in an archive is scrutinized before a single byte is written:
- Traversal tokens (`..`, `/`, `\`) and leading path separators are sanitized.
- Paths resolving outside the destination directory are rejected with `ArchiveFailure.UnsafePath`.
- Reparse points, symlinks, and junctions are skipped or converted to safe local files.
- Password-protected or header-encrypted archives are detected upfront and cleanly failed with `ArchiveFailure.Encrypted` without writing temporary debris to disk.

---

## 4. Format Matrix

| Category | Source Extensions | Target Extensions | Engine |
|---|---|---|---|
| Document | `.docx`, `.rtf`, `.odt`, `.html`, `.md`, `.txt` | `.txt`, `.md`, `.html`, `.docx`, `.rtf`, `.odt`, `.pdf` | `PandocConversionEngine` |
| Spreadsheet | `.csv`, `.json`, `.xlsx` | `.csv`, `.json`, `.xlsx`, `.txt`, `.md` | `SpreadsheetConversionEngine` |
| Archive | `.zip`, `.7z`, `.tar`, `.gz`, `.rar` | `#extract` (folder) | `ArchiveConversionEngine` |
| Directory | `""` (folder) | `.zip`, `.7z`, `.tar`, `.gz` | `ArchiveConversionEngine` |

*Note: RAR is read-only. Producing RAR files is strictly prohibited by license and architectural guards.*

---

## 5. Third-Party Notices & Licenses

1. **Pandoc 3.5:** GPL-2.0-or-later, standalone external executable under `tools/pandoc/`.
2. **wkhtmltopdf 0.12.6-1:** LGPL-3.0, standalone external executable under `tools/wkhtmltopdf/`.
3. **7-Zip (`7za.exe`) 23.01:** LGPL-2.1 with unRAR restriction, standalone external executable under `tools/7z/`.
4. **SharpCompress 0.40.0:** MIT License, in-process managed assembly.
5. **MiniExcel 1.41.0:** Apache-2.0 License, in-process managed assembly.
