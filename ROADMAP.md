# Quick-Convert Roadmap

**Status:** Phase 1 complete, Phase 2 in progress
**Audience:** the four-person development team
**Related documents:** `PHASE_2_SPEC.md` (detailed Phase 2 requirements), `docs/PHASE_1_CLOSEOUT.md`
(current verification status), `THIRD-PARTY-NOTICES.md` (licence obligations)

---

## 1. Product summary

Quick-Convert is an offline file converter integrated into the Windows File Explorer context menu. A user
selects one or more files, chooses a target format, and receives converted files in the same folder. The
application runs without network access, accounts, subscriptions or telemetry.

Target platforms are Windows 10 (1809 or later) and Windows 11. The solution is written in C# on .NET 9
with WPF for the user interface.

The supported format scope is fixed for the first release and deliberately small (approximately 25
formats). Quality and reliability matter more than coverage.

---

## 2. Supported formats

| Category | Input formats | Output formats |
|---|---|---|
| Image | jpg, jpeg, png, webp, bmp, tiff, gif, ico, heic\*, avif\* | jpg, png, webp, bmp, tiff, gif, ico, pdf |
| Audio | mp3, wav, flac, aac, ogg, m4a, wma | mp3, wav, flac, aac, ogg, m4a |
| Video | mp4, mkv, avi, mov, webm, wmv | mp4, mkv, webm, mov, gif, plus audio extraction |
| Document | txt, md, html, docx, rtf, odt, pdf | txt, md, html, docx, pdf, rtf, odt |
| Spreadsheet | csv, json, xlsx | csv, json, xlsx, txt |
| Archive | zip, 7z, tar, gz, rar (read only) | zip, 7z, tar, gz |
| E-book | epub, mobi | epub, pdf, txt |

\* HEIC and AVIF are included only if the selected imaging library can decode them on a clean Windows
installation. They must not appear in the interface before that is confirmed.

**Technology stack:** .NET 9, WPF with WPF-UI, Magick.NET-Q8-AnyCPU for images, FFmpeg for audio and
video, Pandoc for documents, MiniExcel for spreadsheets, SharpCompress and `7za.exe` for archives, Inno
Setup 6 for packaging.

---

## 3. Planning decisions

The following decisions shape the phase order and the technical approach. They are recorded here so the
rationale does not have to be reconstructed later.

| Decision | Rationale |
|---|---|
| One phase numbering for the whole project. Phase 1 covers the application skeleton; Phase 2 covers the first working conversion. | Earlier documents used "Phase 1" for two different things (skeleton and first conversion), which caused the project to be reported as further along than it was. Each phase below therefore states verifiable exit criteria. |
| Engine routing, capability checking and a shared subprocess runner are planned work, not assumed work. | Conversion libraries are the easy part. Selecting the correct engine for a format pair, and preventing the interface from offering unsupported conversions, requires deliberate design. The application currently registers a single conversion engine, which cannot serve several engines. |
| The Explorer context menu is investigated in Phase 2 and completed in Phase 5. | The context menu is the product's main entry point. On Windows 11 legacy registry verbs appear only under "Show more options", while the primary menu requires an `IExplorerCommand` implementation distributed in a signed MSIX package. This affects the inter-process design, so it must be answered before packaging begins (decision D2). |
| Repository source code is MIT licensed. GPL tools are distributed as separate executables. | MIT and GPL cannot be combined as a single licence choice. FFmpeg and Pandoc are separate programs invoked as subprocesses, so their obligations apply to those programs and not to the application source. Notices and a source offer are required. |
| Command-line tools are downloaded by a checksummed script and excluded from Git. | The combined tool set exceeds 100 MB. Committing it would permanently enlarge the repository and slow every clone and CI run. |
| RAR extraction uses SharpCompress; `7za.exe` is used only to create 7z archives. | `7za.exe` has a reduced codec set, and the RAR support in 7-Zip carries the unRAR restriction. SharpCompress is MIT licensed and managed, which removes the restriction from the distributed package. |
| Spreadsheet support uses MiniExcel only. | ClosedXML adds `DocumentFormat.OpenXml` and `SixLabors.Fonts`; the latter is published under the Six Labors Split Licence, which is not unconditionally free. |
| Code signing is deferred to the packaging phase and recorded as a decision gate. | An unsigned installer produces a SmartScreen warning, and the Windows 11 primary context menu requires a signed package. Neither option is free, so this is a product decision rather than a technical one. |

---

## 4. Phases

### Phase 1: Application skeleton and IPC (complete)

**Delivered:** solution and four projects, core models, `IConversionEngine`, single-instance mutex with
named-pipe IPC and a 150 ms debounce, `BatchConversionCoordinator` with concurrency and cancellation, MVVM
shell for the quick popup and the dashboard, licence and third-party notice files, CI workflow, and unit
tests for the core library.

**Exit criteria:** the solution restores and builds on Windows; two launches collapse into a single
instance; a 20-file selection produces one debounced batch; unit tests pass; licence files are present.

**Outstanding:** the CI workflow has not yet produced a run, and the three runtime scenarios have not been
confirmed on a Windows machine. Both items are tracked in `docs/PHASE_1_CLOSEOUT.md`.

### Phase 2: First working conversion and capability model (current)

**Delivered:**

1. `ConversionMatrix`, a single table of allowed conversions replacing the hardcoded format lists in
   `FormatRegistry`.
2. `IConversionEngineRouter`, which selects an engine for a source and target format and reports the
   formats actually available.
3. `QuickConvert.Engines.Images`, a Magick.NET implementation for image formats.
4. Capability checking in the interface, so unsupported conversions cannot be selected.
5. A hardened launch path: no process exit inside the library, retry on connection failure, global
   exception handling and file logging.
6. A single conversion execution path shared by the popup and the dashboard.
7. A timeboxed investigation of the Explorer context menu (decision D2).

Detailed requirements are in `PHASE_2_SPEC.md`.

**Exit criteria:** a JPEG converts to a real PNG from the context menu and the output opens in the Windows
Photos application; ten files convert in parallel with no duplicate output names; cancelling leaves no
partial files; a ZIP-to-PNG request is refused in the interface; RAR is never offered as an output format;
the context menu investigation produces measured numbers; CI is green.

### Phase 3: Media engine

**Delivered:** audio and video conversion through FFmpeg, hardware acceleration (NVENC, QSV, AMF) with
software fallback, audio extraction from video, progress reported through `-progress pipe:1`, graceful
cancellation, and a shared subprocess runner (argument quoting without a shell, per-job temporary
directories, timeouts, stderr capture).

**Exit criteria:** MP4 to MP3 and an MP4 to WEBM to MP4 round trip both play correctly; the progress bar
tracks a 30-second clip within 5 percentage points; cancellation leaves no partial output and no orphaned
`ffmpeg.exe` process; a missing tool binary produces a clear message rather than a crash; decision D1 is
resolved and the notices are updated.

### Phase 4: Documents and archives

**Delivered:** document conversion through Pandoc, PDF output, spreadsheet conversion through MiniExcel,
archive handling through SharpCompress and `7za.exe`.

**Exit criteria:** a 5 MB DOCX to PDF conversion opens in a PDF viewer; a 100 MB folder survives a
ZIP to 7z to extracted round trip with identical file count and hashes; RAR extraction works and RAR
cannot be selected as an output format; a corrupt archive produces a message rather than an unhandled
exception.

### Phase 5: Shell integration and packaging

**Delivered:** the production context-menu entry point (classic verb or `IExplorerCommand`, per decision
D2), multi-selection handling, a per-user Inno Setup installer that does not require administrator rights,
file associations and icons, uninstallation that removes the registry entries, and a first-run check for
the bundled tools.

**Exit criteria:** a clean Windows virtual machine can install, convert from the context menu and uninstall
without leaving registry entries or files behind; a 20-file selection works from the real context menu;
installer size and installation time are recorded; the SmartScreen behaviour is documented.

### Phase 6: Version 1.0 hardening

**Delivered:** dark and light themes, system tray integration, local crash reporting, output folder policy,
performance work, accessibility review, and user documentation.

**Exit criteria:** the offline audit passes (no HTTP client anywhere in the dependency graph); cold start
to a visible popup is under 500 ms; a full conversion run produces no network traffic in a packet capture.

---

## 5. Decision gates

These decisions must be resolved before the phase that depends on them.

| ID | Decision | Required by | Options | Default |
|---|---|---|---|---|
| D1 | FFmpeg build: GPL or LGPL | Phase 3 | GPL build with x264 (best H.264 quality, requires publishing FFmpeg source and build configuration) or LGPL build without x264 (H.264 encoding through hardware or Media Foundation) | GPL build; the repository is public, so the compliance work is small |
| D2 | Context menu implementation | Phase 2 investigation, Phase 5 delivery | Classic `HKCU` registry verb (appears under "Show more options" on Windows 11), `IExplorerCommand` with a signed MSIX package, or the classic verb in version 1.0 followed by the modern menu in 1.1 | Classic verb |
| D3 | HEIC and AVIF support | Phase 2 | Include only if the imaging library decodes real sample files on a clean machine | Excluded, may be added later |
| D4 | Code signing | Phase 5 | Unsigned with documented SmartScreen instructions, OV certificate, or EV certificate | Unsigned for version 1.0 |

---

## 6. Risks

| Risk | Likelihood | Impact | Mitigation |
|---|---|---|---|
| Users do not find the context menu entry on Windows 11 because it is under "Show more options" | High | High | Resolve D2 during Phase 2; provide additional entry points (tray icon, Start menu shortcut, conversion from the dashboard); consider MSIX in version 1.1 |
| A 20-file selection starts 20 processes, each with a WPF cold start | High | Medium | The 150 ms debounce already merges them, but startup cost remains. Enable ReadyToRun and single-file publishing, measure during the Phase 2 investigation, and set `MultiSelectModel` on the verb so Explorer limits the number of launches |
| The Magick.NET native payload makes the installer large | Medium | Medium | Measure during Phase 2; use the Q8 build rather than Q16; remove unused formats if necessary |
| GPL compliance is missed when FFmpeg and Pandoc are bundled | Medium | High | Add notices and the source offer when the tools are first bundled in Phase 3; add a CI check that every file in `tools/` has a notice entry |
| A licence problem enters through a transitive package | Medium | High | Every new package requires a licence, size and justification note in the pull request |
| Long video conversions appear unresponsive on slower machines | Medium | Medium | Show an estimated time from duration and bitrate, explain that the conversion may take several minutes, and detect hardware acceleration |
| Cloud placeholder, read-only or locked input files | High | Medium | Work through the input checklist in section 7 during Phase 2 |
| Cancellation leaves an orphaned `ffmpeg.exe` process | Medium | High | Keep a job-scoped `Process` reference and call `Kill(entireProcessTree: true)` in a `finally` block |
| Scope grows back towards hundreds of formats | High | High | The conversion matrix in Phase 2 is the control point: adding a format means adding a row and a test |

---

## 7. Input and environment checklist

These cases must be handled before packaging in Phase 5.

- [ ] File open in another application (locked): clear message, no crash
- [ ] Read-only source file or read-only destination folder: clear message
- [ ] OneDrive or Dropbox placeholder that has not been downloaded locally
- [ ] Path longer than 260 characters: handled or clearly refused
- [ ] UNC path (`\\server\share\...`): supported or refused before any work starts
- [ ] Name collision in the destination folder: `photo (1).png`
- [ ] Empty file, corrupt file, or a file whose extension does not match its content
- [ ] Insufficient disk space during a 2 GB video conversion: fails cleanly and deletes the partial output
- [ ] 20-file selection from a real Explorer window
- [ ] Second conversion request while a batch is running: the request joins the queue
- [ ] Application terminated mid-batch: no corrupted outputs, clean next launch
- [ ] Display scaling at 150% and 200%, secondary monitor, right-to-left languages
- [ ] Non-administrator account converting a file under `C:\Program Files`: refused with a reason

---

## 8. Working agreement

- One concern per pull request. Prefix the subject with `core:`, `img:`, `media:`, `docs:`, `ui:`,
  `platform:` or `pkg:`.
- Changes to `IConversionEngine`, `ConversionJob`, `ConversionOptions`, `ConversionResult` or
  `IConversionEngineRouter` require three approvals. All other changes require one.
- Every new dependency requires a licence, size and justification note in the pull request description.
  This is what keeps the project free to distribute.
- CI must be green on `windows-latest` before merging. Direct pushes to `main` are not permitted.
- If the specification turns out to be wrong, update it in the same pull request. An out-of-date
  specification causes more problems than having none.

---

## 9. Out of scope

Cloud storage and accounts; telemetry or automatic crash upload; optical character recognition; subtitle
processing; DRM-protected media; RAR creation; hundreds of legacy formats; macOS and Linux; automatic
updates in version 1.0; plugins; scheduled batch jobs.
