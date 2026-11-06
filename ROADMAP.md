# Quick-Convert — Roadmap

> Replaces the original 5-step plan. Same product, same team, same $0 constraint — but the phases now
> have exit criteria, the ordering risk is fixed (shell integration moved earlier), and three licence
> traps the old plan would have hit are resolved up front.
>
> **Read `docs/AUDIT_PHASE_1.md` for the code-level evidence behind several of these changes.**

---

## 1. Why this roadmap was rewritten

The old plan was a good instinct ("vertical slice first") with four structural problems that would have
cost us weeks:

| # | Flaw in the old plan | What it would have cost | Fix in this roadmap |
|---|---|---|---|
| 1 | "Phase 1 = vertical slice" in one document and "Phase 1 = scaffolding" in another. | Exactly what happened: Phase 1 was declared done while zero conversions existed. | Phases are numbered once, here. **Phase 1 = skeleton (done). Phase 2 = first real conversion.** Every phase has exit criteria a stranger can verify. |
| 2 | "Once that loop works, everything else is just adding more engines." | The routing/capability layer is the hard part, not the engines. A single `IConversionEngine` singleton is wired in DI today; engine #2 makes engine #1 unreachable. | Engine **routing + capability gating + subprocess runner** are explicit Phase 2/3 work, not assumed. |
| 3 | Explorer integration left until Step 5 ("Packaging & Shell Integration"). | The entire value proposition is the right-click menu. Windows 11 puts classic verbs behind *Show more options*, and the modern menu needs an `IExplorerCommand` + MSIX sparse package. Discovering that in the last step re-opens the IPC design. | Shell **spike in Phase 2** (dev-only `HKCU` verb, no installer), full integration in Phase 5. Decision gate D2 recorded now. |
| 4 | "MIT/GPL license right from the first commit." | MIT and GPL are not one choice, and there is no `LICENSE` file in the repo at all. Shipping FFmpeg/Pandoc binaries raises real obligations. | Repo code is **MIT**; GPL tools are separate programs (mere aggregation) with notices + source offer. `LICENSE` and `THIRD-PARTY-NOTICES.md` now exist. |
| 5 | "Bundle the essential CLI tools directly into the repo /tools." | +100 MB in Git history, permanently; every clone and every CI run pays for it. | `tools/` binaries are **gitignored**, fetched by a checksummed script at package time. |
| 6 | "MiniExcel/ClosedXML" as interchangeable. | Not interchangeable: ClosedXML drags in `DocumentFormat.OpenXml` + `SixLabors.Fonts` (Six Labors Split Licence) on top of the size. | **MiniExcel only** (Apache-2.0). ClosedXML is out. |
| 7 | "SharpCompress and 7za.exe … we can read RAR." | `7za.exe` ships a reduced codec set; RAR is the `7z.dll`/unRAR side, which carries the unRAR restriction. | **SharpCompress** (MIT, pure managed) reads RAR; `7za.exe` writes 7z. The unRAR question disappears from the bundle. |
| 8 | "$0 and open-source tools" assumed to mean "$0 to ship". | An unsigned installer triggers SmartScreen; the Win11 modern context menu needs a signed MSIX. Neither is free. | Recorded as decision gate D4, with a v1 path that stays $0 (per-user Inno Setup, accept the SmartScreen warning, classic verb). |
| 9 | No exit criteria, no demo, no tests, no CI, no real-world edge cases. | "Done" means whatever the last person to commit says it means. | Every phase has a demo script + tests + CI gate; edge-case checklist in §6. |

---

## 2. Product frame (unchanged)

Offline, $0, no accounts, no telemetry, no network at runtime. ~25 essential formats. Windows 10 1809+ /
Windows 11. Right-click in File Explorer, convert, done.

**Supported scope, frozen:**

| Category | Sources | Targets |
|---|---|---|
| Image | jpg, jpeg, png, webp, bmp, tiff, gif, ico, heic\*, avif\* | jpg, png, webp, bmp, tiff, gif, ico, pdf |
| Audio | mp3, wav, flac, aac, ogg, m4a, wma | mp3, wav, flac, aac, ogg, m4a |
| Video | mp4, mkv, avi, mov, webm, wmv | mp4, mkv, webm, mov, gif, + audio extraction |
| Document | txt, md, html, docx, rtf, odt, pdf | txt, md, html, docx, pdf, rtf, odt |
| Spreadsheet | csv, json, xlsx | csv, json, xlsx, txt |
| Archive | zip, 7z, tar, gz, **rar (read only)** | zip, 7z, tar, gz |
| E-book | epub, mobi | epub, pdf, txt |

`*` only if Magick.NET's native build carries the delegate — verify in Phase 2, do not advertise before.

**Stack, frozen:** .NET 9 · WPF + WPF-UI · Magick.NET-Q8-AnyCPU (images) · FFmpeg (media) · Pandoc
(documents) · MiniExcel (sheets) · SharpCompress + `7za.exe` (archives) · Inno Setup 6 (installer).

---

## 3. Phases

### Phase 1 — Skeleton, IPC, contract ✅ DONE
Solution and four projects, models, `IConversionEngine`, single-instance mutex + named pipe with 150 ms
debounce, `BatchConversionCoordinator`, MVVM shell for popup + dashboard, `LICENSE` + notices, CI on
`windows-latest`, unit tests for Core.

**Exit criteria (met):** solution restores and builds on Windows · two launches collapse into one
instance · 20 files produce one debounced batch · tests green in CI · licence files present.

### Phase 2 — First real conversion (JPG→PNG) + capability model ← **current**
Six deliverables, in this order (full detail in `PHASE_2_SPEC.md`):

1. `ConversionMatrix` — one table, our 25 formats, `rar` never a target.
2. `IConversionEngineRouter` — `(source, target) → engine?`; nothing outside the coordinator calls
   `ConvertAsync`; `CanConvert` finally gets called.
3. `QuickConvert.Engines.Images` on Magick.NET — jpg/png/webp/bmp/tiff/gif/ico, `AutoOrient()`,
   unique output names, real progress, cancel leaves no debris.
4. Capability gating in the UI — a user can never pick a conversion that will fail.
5. Hardened launch path — no `Environment.Exit` in the library, bounded retry, `CurrentUserOnly` pipe,
   global exception handler, rolling log file.
6. **Shell spike (timebox: half a day)** — per-user `HKCU\Software\Classes\SystemFileAssociations\...\shell`
   verb pointed at the built exe, `MultiSelectModel` set, test with 1 and 20 files, measure time-to-popup.
   This is throwaway code; its job is to answer decision gate D2 and nothing else.

**Exit criteria:** a real JPG converts to a real PNG from Explorer, output opens in Photos · 10 files
convert in parallel, no duplicate names · cancelling leaves no partial files · ZIP→PNG is refused in the
UI · RAR is never offered as a target · the shell spike answers D2 with numbers · CI green.

### Phase 3 — Media engine (FFmpeg)
Audio + video transcoding, hardware acceleration (NVENC / QSV / AMF) with CPU fallback, audio extraction
from video, thumbnail-free (`gif`) conversion. Progress from `-progress pipe:1`, not stderr scraping.
Graceful cancellation (short `q` keypress / `SIGTERM` equivalent before kill). Subprocess runner shared
with Phase 4: argument quoting, no shell, per-job temp dirs, timeout, stderr capture to log.

**Exit criteria:** MP4→MP3 and MP4→WEBM→MP4 round-trip play correctly · progress bar tracks a 30 s clip
within ±5 % · cancel mid-transcode leaves no partial output and no orphaned `ffmpeg.exe` · a missing
`tools/ffmpeg/ffmpeg.exe` produces a clear message, not a crash · D1 (GPL vs LGPL build) decided and
notices updated.

### Phase 4 — Documents & archives
Pandoc for md/html/docx/rtf/odt/txt, PDF out via Magick.NET/PdfSharp, MiniExcel for csv/json/xlsx,
SharpCompress for zip/tar/gz/RAR-read, `7za.exe` for 7z write.

**Exit criteria:** 5 MB docx→pdf opens in a PDF viewer · 100 MB folder zipped→7z→unzipped byte-identical
(file count + hashes) · RAR extracts, RAR is impossible to select as output · corrupt archive produces a
message, not an exception dialog.

### Phase 5 — Shell integration & packaging
Production verb (classic or `IExplorerCommand` per D2), multi-select handling, Inno Setup installer
(per-user, no admin prompt), file associations + icons, uninstall that removes the verb, first-run
tool check.

**Exit criteria:** clean Windows VM → install → right-click → convert → uninstall → no leftover registry
keys or files · 20-file multi-select works from the real context menu · installer size and install time
recorded · no SmartScreen surprise undocumented.

### Phase 6 — v1.0 hardening & polish
Dark/light theming, tray icon, crash reporting (local file only), output-folder policy, i18n-ready
strings, performance pass (startup budget ≤ 500 ms to popup), accessibility, docs + demo GIF.

**Exit criteria:** offline audit passes (no HTTP client anywhere in the dependency graph) · cold start
to visible popup ≤ 500 ms · a full conversion run leaves zero network traffic in a packet capture.

---

## 4. Decision gates (decide before the phase that needs them)

| ID | Decision | Needed by | Options | Default if undecided |
|---|---|---|---|---|
| **D1** | FFmpeg build: GPL or LGPL | Phase 3 | (a) GPL build with x264 — best H.264 quality, obliges us to publish FFmpeg source + build config; (b) LGPL build, no x264, H.264 encode via hardware (NVENC/QSV/AMF) or Media Foundation | (a) — it is the pragmatic choice; a public repo is already the compliance posture |
| **D2** | Context menu model | Phase 2 (spike) → Phase 5 | (a) Classic registry verb (`HKCU`), lives under "Show more options" on Win 11; (b) `IExplorerCommand` + MSIX sparse package for the modern menu, needs signing; (c) ship (a) in v1, add (b) in v1.1 | (a) |
| **D3** | HEIC/AVIF in or out | Phase 2 | include only if Magick.NET decodes real sample files on a clean machine | out, advertised later |
| **D4** | Code signing | Phase 5 | (a) unsigned, document the SmartScreen "More info → Run anyway" step; (b) OV cert; (c) EV cert | (a) for v1 |

---

## 5. Risk register

| Risk | Likelihood | Impact | Mitigation |
|---|---|---|---|
| Shell verb killed by Windows 11's "Show more options" (users never find it) | High | High | D2 spike in Phase 2; ship a tray icon + Start-menu shortcut + "convert from dashboard" as parallel entry points; consider MSIX in v1.1 |
| 20 files = 20 process launches, each a WPF cold start | High | Medium | Debounce already collapses them, but 20 startups are still 20 startups: `PublishReadyToRun` + single-file publish, measure in the Phase 2 spike, and set `MultiSelectModel=Player` (≤15) or `Document` (≤100) on the verb so Explorer itself caps the fan-out |
| Magick.NET native payload bloats the installer | Medium | Medium | Measure in Phase 2; Q8 not Q16; trim unused formats if needed |
| FFmpeg/Pandoc GPL compliance slip | Medium | High | Notices + source offer at first bundle (Phase 3), CI check that `tools/` contents have a notice entry |
| Licence trap sneaks in via a transitive NuGet | Medium | High | Every new package needs a licence line in the PR; CI could fail on unknown licences later |
| Video transcode on weak CPUs looks broken (long, silent) | Medium | Medium | ETA from duration/bitrate, "this may take a few minutes" copy, hardware accel detection |
| OneDrive placeholder / read-only / locked files | High | Medium | Edge-case checklist §6 in Phase 2 |
| Cancellation leaves orphaned `ffmpeg.exe` | Medium | High | Job-scoped `Process` handle + `Kill(entireProcessTree: true)` in `finally` |
| Scope creep back to "300 formats" | High | High | The matrix in Phase 2 is the gate; adding a format = adding a row + a test |

---

## 6. Real-world input checklist (must pass before Phase 5)

- [ ] File open in another app (locked) → clear message, no crash
- [ ] Read-only source, read-only output folder → clear message
- [ ] OneDrive/Dropbox placeholder (not hydrated) → triggers download or explains
- [ ] Path > 260 chars → handled or clearly refused
- [ ] UNC path `\\server\share\...` → works or refuses up front
- [ ] Source and output on the same folder, name collision → `photo (1).png`
- [ ] 0-byte file, corrupt file, wrong extension (a .png that is really a jpg) → message, not exception
- [ ] Low disk space during a 2 GB video conversion → fails cleanly, deletes partial
- [ ] 20-file multi-select from a real Explorer window
- [ ] Convert while a batch is already running (second right-click) → joins the queue
- [ ] App killed mid-batch (Task Manager) → no corrupted outputs left behind, next launch is clean
- [ ] High DPI (150 %, 200 %), secondary monitor, RTL languages
- [ ] Non-admin user, folder under `C:\Program Files` → refuses with a reason

---

## 7. Working agreement (4 people, Phase Relay)

- One concern per PR. Prefix: `core:` `img:` `media:` `docs:` `ui:` `platform:` `pkg:`.
- Contract changes (`IConversionEngine`, `ConversionJob`, `ConversionOptions`, `ConversionResult`,
  `IConversionEngineRouter`) need three approvals; everything else needs one.
- Every new dependency: licence + size + why, in the PR description. No exceptions, this is how the
  "$0" promise survives.
- CI must be green on `windows-latest` to merge. No direct pushes to `main`.
- If you find the spec wrong, change the spec in the same PR. A stale spec is worse than none — that is
  literally how Phase 1's hand-off document ended up as 9 KB of null bytes.

---

## 8. Deliberately out of scope

Cloud anything · accounts · telemetry/crash upload · OCR · subtitle burn-in/translation · DRM'd media ·
RAR creation · 300 obsolete formats · macOS/Linux (the shell integration is the product) · auto-update in
v1 · plugins/extensions · batch presets with scheduling.
