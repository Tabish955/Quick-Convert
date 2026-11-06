# PHASE 2 SPEC — Real Conversion & Hardened Shell Path

> **Status:** active. This file replaces the previous `PHASE_2_SPEC.md`, which was committed as
> 9,454 bytes of NUL characters and could not be recovered from Git history (the repo has a single
> commit). Content below is derived from the code at `c9e1a32` plus the scope decisions in
> `docs/AUDIT_PHASE_1.md`.
>
> **Read `docs/AUDIT_PHASE_1.md` first.** It contains the evidence for every claim here.

---

## 0. Ground rules (do not re-litigate these in Phase 2)

> **Phase 1 is closed** as of this document: `LICENSE`, `THIRD-PARTY-NOTICES.md`, `.editorconfig`,
> `Directory.Build.props`, the `tests/QuickConvert.Core.Tests` project and the Windows CI workflow are
> in the repo. `dotnet build` / `dotnet test` still need one run on a Windows machine before Phase 2
> starts — see `docs/PHASE_1_BUILD_CHECKLIST.md`.
>
> Product-level plan, decision gates and risks live in `ROADMAP.md`. Verification status of Phase 1 (including
> what is still unconfirmed: CI has never run, runtime scenarios untested) is in `docs/PHASE_1_CLOSEOUT.md`.
> This file stays scoped to Phase 2 work.

1. **Scope is ~25 essential formats**, not 300. JPG, PNG, WEBP, BMP, TIFF, GIF, MP4, MKV, WEBM, MP3, WAV, FLAC, AAC, OGG, M4A, PDF, TXT, MD, HTML, DOCX, CSV, JSON, ZIP, 7Z, TAR, GZ. E-book later.
2. **RAR is extract-only.** `.rar` may appear as a *source*, never as a *target*. This is a licence obligation (unRAR restriction), not a preference. A unit test must enforce it.
3. **Offline, $0, no telemetry, no network calls at runtime.** Ever.
4. **One execution path.** The quick popup and the dashboard must both go through `BatchConversionCoordinator`. No second `await engine.ConvertAsync(...)` loop in a ViewModel.
5. **No UI may advertise a conversion the engines cannot perform.** `CanConvert` must be consulted before a job is enqueued.
6. **`FormatRegistry` is not the product scope.** It is a lookup table; the product scope is the matrix in §2.

---

## 1. Objective

Phase 1 produced working plumbing (single instance, IPC, MVVM shell, batch coordinator) and a mock engine. Phase 2 produces **one real conversion path** that a user can trust, and hardens the launch path that carries the user's file selection into the app.

**Phase 2 is done when** a reviewer can right-click one or ten JPEGs in File Explorer, pick PNG/WEBP, get real images, cancel mid-batch without debris, and never sees the app claim a format it cannot produce.

Non-goals for Phase 2: FFmpeg/audio/video, Pandoc/documents, the Explorer COM shell extension, the installer. Those are Phases 3–5.

---

## 2. Deliverable 1 — `ConversionMatrix` (single source of truth)

**Owner: Core. Blocks everyone else. Do this first.**

Replace the per-category lists in `FormatRegistry` with one explicit table, and delete everything it does not contain.

```csharp
namespace QuickConvert.Core.Services;

public static class ConversionMatrix
{
    // source ext -> allowed target exts (empty = extraction/consumption only)
    private static readonly Dictionary<string, string[]> Matrix = new(StringComparer.OrdinalIgnoreCase)
    {
        // --- images -------------------------------------------------------------
        [".jpg"]  = [".png", ".webp", ".bmp", ".tiff", ".gif", ".pdf"],
        [".jpeg"] = [".png", ".webp", ".bmp", ".tiff", ".gif", ".pdf"],
        [".png"]  = [".jpg", ".webp", ".bmp", ".tiff", ".gif", ".pdf"],
        [".webp"] = [".jpg", ".png", ".bmp", ".tiff", ".gif", ".pdf"],
        [".bmp"]  = [".jpg", ".png", ".webp", ".tiff", ".gif", ".pdf"],
        [".tiff"] = [".jpg", ".png", ".webp", ".bmp", ".gif", ".pdf"],
        [".gif"]  = [".jpg", ".png", ".webp", ".bmp", ".tiff", ".pdf"],

        // --- audio (Phase 3) ----------------------------------------------------
        [".mp3"] = [".wav", ".flac", ".aac", ".ogg", ".m4a"],
        [".wav"] = [".mp3", ".flac", ".aac", ".ogg", ".m4a"],
        [".flac"] = [".mp3", ".wav", ".aac", ".ogg", ".m4a"],
        [".aac"] = [".mp3", ".wav", ".flac", ".ogg", ".m4a"],
        [".ogg"] = [".mp3", ".wav", ".flac", ".aac", ".m4a"],
        [".m4a"] = [".mp3", ".wav", ".flac", ".aac", ".ogg"],
        [".wma"] = [".mp3", ".wav", ".flac"],

        // --- video (Phase 3) ----------------------------------------------------
        [".mp4"]  = [".mkv", ".webm", ".mov", ".gif", ".mp3", ".wav", ".m4a"],
        [".mkv"]  = [".mp4", ".webm", ".mov", ".gif", ".mp3", ".wav", ".m4a"],
        [".avi"]  = [".mp4", ".mkv", ".webm", ".mov", ".gif", ".mp3", ".wav"],
        [".mov"]  = [".mp4", ".mkv", ".webm", ".gif", ".mp3", ".wav"],
        [".webm"] = [".mp4", ".mkv", ".mov", ".gif", ".mp3", ".wav"],
        [".wmv"]  = [".mp4", ".mkv", ".gif", ".mp3", ".wav"],

        // --- documents (Phase 4) ------------------------------------------------
        [".txt"]  = [".md", ".html", ".pdf", ".docx", ".csv"],
        [".md"]   = [".txt", ".html", ".pdf", ".docx"],
        [".html"] = [".txt", ".md", ".pdf", ".docx"],
        [".csv"]  = [".json", ".txt", ".xlsx", ".md"],
        [".json"] = [".csv", ".txt", ".yaml"],
        [".docx"] = [".pdf", ".txt", ".md", ".html", ".rtf", ".odt"],
        [".rtf"]  = [".txt", ".html", ".pdf", ".docx"],
        [".odt"]  = [".txt", ".html", ".pdf", ".docx"],
        [".pdf"]  = [".txt", ".png", ".jpg"],   // text extraction / first page only

        // --- archives (Phase 4) -------------------------------------------------
        [".zip"] = [".7z", ".tar", ".gz"],
        [".7z"]  = [".zip", ".tar"],
        [".tar"] = [".zip", ".7z", ".gz"],
        [".gz"]  = [".zip", ".7z", ".tar"],
        [".rar"] = [".zip", ".7z", ".tar"],     // extract-only source. NEVER a target.
    };

    public static bool IsKnownSource(string ext) => Matrix.ContainsKey(Normalize(ext));

    public static IReadOnlyList<string> GetTargets(string sourceExt) =>
        Matrix.TryGetValue(Normalize(sourceExt), out var t) ? t : [];

    public static bool IsAllowed(string sourceExt, string targetExt)
    {
        var target = Normalize(targetExt);
        return GetTargets(sourceExt).Contains(target, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Extensions this matrix must never offer as output (licence/feasibility).</summary>
    public static IReadOnlySet<string> ForbiddenTargets { get; } =
        new HashSet<string>([".rar"], StringComparer.OrdinalIgnoreCase);

    private static string Normalize(string? ext) =>
        string.IsNullOrWhiteSpace(ext) ? string.Empty
        : ext.Trim().ToLowerInvariant().StartsWith('.') ? ext.Trim().ToLowerInvariant()
        : "." + ext.Trim().ToLowerInvariant();
}
```

**Acceptance criteria**

- [ ] `FormatRegistry` has no hardcoded per-category format arrays left; it delegates to `ConversionMatrix`.
- [ ] `ForbiddenTargets` is asserted by a test: `Assert.DoesNotContain(".rar", allTargets)`.
- [ ] Every extension in the matrix has a `FileCategory` (no `Unknown` for a known source).
- [ ] Removing an extension from the matrix removes it from every combo box with no other code change.

---

## 3. Deliverable 2 — Engine router (blocks engine #2 onwards)

**Owner: Core.**

`IConversionEngine` is currently resolved as a single singleton (`App.xaml.cs:67`). That cannot survive four engines. Introduce:

```csharp
public interface IConversionEngineRouter
{
    IConversionEngine? Resolve(string sourceExt, string targetExt);
    IReadOnlyList<string> GetAvailableTargets(string sourceExt); // matrix ∩ engine capability
    string EngineNameFor(string sourceExt, string targetExt);
}
```

- Registered as a **singleton** over `IEnumerable<IConversionEngine>`; engines are registered as `IConversionEngine` multiples.
- `GetAvailableTargets` is what the ViewModels bind to. Nothing else may read `FormatRegistry`/`ConversionMatrix` for UI lists.
- Coordinator and popup call `Resolve(...)`; if `null`, the job fails **before** enqueue with a user-facing reason ("MP4 → PNG is not supported. Convert to MP3 or use the dashboard.").

**Acceptance criteria**

- [ ] Two engines registered simultaneously, each receives only its own files (verified by a fake engine in tests).
- [ ] No `await engine.ConvertAsync` call exists outside `BatchConversionCoordinator`.
- [ ] `IConversionEngine.CanConvert` is called at least once per enqueue (no dead contract).

---

## 4. Deliverable 3 — Real image engine (the vertical slice)

**Owner: Image engine. Owner: UI is blocked on this for §6 acceptance.**

New project `src/QuickConvert.Engines.Images` using **Magick.NET** (`Magick.NET-Q8-AnyCPU`, Apache-2.0 wrapper over ImageMagick). Chosen by the team over SkiaSharp (see `docs/AUDIT_PHASE_1.md` §4 for the trade-off) because one library covers rasters, WEBP, AVIF/HEIC and ICO — which is exactly the scope we agreed. Do **not** use ImageSharp: its licence is the Six Labors Split Licence, which is not unconditionally free.

| Concern | Decision |
|---|---|
| Library | `Magick.NET-Q8-AnyCPU` (Apache-2.0). Adds a large native payload — check the Inno Setup size budget before Phase 5 |
| Formats in | jpg, jpeg, png, webp, bmp, tiff, gif (first frame), ico; heic/avif only after confirming the delegate is present in the build |
| Formats out | jpg, png, webp, bmp, tiff, gif (single frame), ico, pdf (via PdfSharp / Magick.NET `Write("pdf")`) |
| Quality | `ConversionOptions.Quality` (1–100) → JPEG/WebP encoder quality |
| Type | `IConversionEngine`, `net9.0` |
| Progress | 0–10 decode, 10–85 encode, 85–100 flush/close (real, not simulated) |
| Cancellation | `ct.ThrowIfCancellationRequested()` between decode/encode, delete the partial output on cancel |
| EXIF | Orientation applied via `MagickImageInfo.Orientation` / `image.AutoOrient()` (tests must cover it) |
| Output naming | `photo.png` → `photo (1).png`, `photo (2).png`; never overwrite unless `OverwriteExisting` |
| Failure modes | Unreadable source → `ConversionResult(Success:false, ErrorMessage:"…")` with the reason; never throw out of `ConvertAsync` |

**Acceptance criteria**

- [ ] A 4000×3000 JPEG converts to PNG in < 2 s on a normal laptop, output opens in Photos.
- [ ] Same source → WEBP produces a file starting with `RIFF….WEBP`.
- [ ] A phone photo with `Orientation=6` is not rotated after conversion.
- [ ] Cancel mid-encode leaves **no** partial file in the output folder.
- [ ] `MockConversionEngine` is no longer registered in any DI path that ships.

---

## 5. Deliverable 4 — Hardened launch path + observability

**Owner: Platform.**

1. `SingleInstancePipeService`:
   - remove `Environment.Exit` from the library; return `bool`/`SendResult`;
   - bounded retry (5 attempts, 60 ms back-off) before giving up;
   - server created with `PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly`;
   - keep the 150 ms debounce and the duplicate-path `HashSet`.
2. `App.OnStartup`: wrap in `try/catch`; hook `DispatcherUnhandledException` + `AppDomain.CurrentDomain.UnhandledException`; log and show a message instead of dying.
3. Logging: rolling file at `%LOCALAPPDATA%\QuickConvert\logs\quickconvert-{date}.log`, 7-day retention, no file paths at `Info` level (privacy), full detail at `Debug`.
4. If the launch handshake fails, the secondary instance shows a small window/balloon ("QuickConvert is starting…") and exits — never a silent no-op.

**Acceptance criteria**

- [ ] Killing the primary mid-handshake, then right-clicking 3 files, produces a visible explanation and a log entry containing the failure reason.
- [ ] `Global\` mutex path still verified for two users on the same machine.
- [ ] A thrown exception in a command handler produces a log file + dialog, not a vanished process.

---

## 6. Deliverable 5 — One execution path + popup correctness

**Owner: UI.**

- `QuickPopupViewModel` drops `IConversionEngine` and uses `BatchConversionCoordinator` (so concurrency, cancellation and dashboard counters are shared). Keep the popup's compact progress contract by subscribing to coordinator events.
- Mixed selections with an empty intersection show an explicit message + "Open Dashboard", never the first file's target list.
- Popup placement: convert `GetCursorPos` (physical px) to the right monitor's work area in DIUs; support negative `Left` (monitors left of primary) and per-monitor DPI.
- Dark/light theme switch (`ui:ThemesDictionary`), tray icon (`Hardcodet.NotifyIcon.Wpf` is already referenced and currently unused) or remove the package.
- Rename `AddFiles(IEnumerable<string>)` → `AddFilePaths` to avoid shadowing the generated command.

**Acceptance criteria**

- [ ] Popup appears next to the cursor at 100 %, 150 % and 200 % scaling, on primary and secondary monitors.
- [ ] A conversion started in the popup is visible in the dashboard with the same job id.
- [ ] Selecting 5 JPGs + 1 ZIP shows "no common target", and the ZIP is never queued.

---

## 7. Work breakdown by relay order

| # | Task | Owner | Depends on | Est. |
|---|---|---|---|---|
| 1 | `ConversionMatrix` + `FileCategory` mapping + tests | Core | — | 0.5 d |
| 2 | `IConversionEngineRouter` + DI wiring + fake-engine tests | Core | 1 | 0.5 d |
| 3 | `Engines.Images` Magick.NET engine (convert, AutoOrient, naming, cancel) | Image | 1 | 2 d |
| 4 | Route popup + coordinator through the router; pre-flight validation | Core/UI | 2, 3 | 0.5 d |
| 5 | IPC hardening + logging + global exception handlers | Platform | — | 1 d |
| 6 | Popup unification + placement + mixed selection | UI | 4 | 1 d |
| 7 | Theming/tray (optional, last) | UI | 6 | 0.5 d |
| 8 | ~~CI + tests project~~ **done in Phase 1**; now: keep CI green and add a test with every task below | Platform | — | ongoing |

Tasks 1, 2, 5, 8 can run in parallel. Task 3 needs 1 only. **Do not start 7 before 4 and 6 are merged.**

---

## 8. PR / review checklist (every Phase 2 PR)

- [ ] One concern per PR; subject prefixed `core:`, `img:`, `ui:`, `platform:`.
- [ ] No new `PackageReference` without a licence note in the PR description (§5 of the audit).
- [ ] New public API in Core = changes to `IConversionEngine`/`ConversionJob`/`ConversionOptions` → requires 3 approvals.
- [ ] `dotnet build QuickConvert.sln -c Release` green (CI enforces).
- [ ] If behaviour changed, a test changed with it.
- [ ] No `catch { }` without a log line.
