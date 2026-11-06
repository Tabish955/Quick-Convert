# Phase 1 Audit — Quick-Convert

**Reviewed commit:** `c9e1a32` (`main`, 35 files / 2,627 insertions, 1 commit)
**Review date:** 2026-11-06
**Reviewer:** internal code review
**Update (2026-11-06):** the team has since selected **Magick.NET** (`Magick.NET-Q8-AnyCPU`, Apache-2.0) for the image engine, and FFmpeg + Pandoc bundled binaries for media/docs. Where this audit suggests SkiaSharp, read Magick.NET; the licence analysis in §5 still applies as written.

**Update 2 (2026-11-06) — Phase 1 closed.** `LICENSE`, `THIRD-PARTY-NOTICES.md`, `.editorconfig`, `Directory.Build.props`, the `tests/QuickConvert.Core.Tests` project (CI is wired to run it) and `tools/README.md` (binaries stay out of Git) were added, and `CancelJob` no longer ignores `Pending` jobs. The "no tests / no CI / no LICENSE" findings below are therefore historical. `ROADMAP.md` is the live plan; the Phase 1 items still open are a CI run and the three runtime checks on Windows (`docs/PHASE_1_BUILD_CHECKLIST.md`).

**Method:** static review of every file in the repository (C#, XAML, csproj, sln, docs). No build or runtime execution was possible in the review environment (no .NET SDK, and WPF cannot build off Windows), so every claim below is backed by the code text, not by a test run. Items that must be confirmed on a Windows machine are marked **[verify-on-build]**.

---

## 1. Verdict in one table

| Area | State | Notes |
|---|---|---|
| Solution / project layout | ✅ Good | Core / Engines.Media / Engines.Docs / UI separation is clean and is the right shape |
| MVVM + DI plumbing | 🟡 Works on paper | DI, `ObservableObject`, `IRelayCommand` all wired; startup is `async void` |
| Single-instance + Named-Pipe IPC | 🟡 Functional, fragile | Debounce logic is sound; failure path silently drops the user's file list |
| `FormatRegistry` (format matrix) | 🔴 Not trustworthy | 64 entries advertise capability no engine implements; `CanConvert` is never called |
| Real conversion (image/audio/video/doc) | 🔴 **Does not exist** | See §2 — the "JPG→PNG vertical slice" is not in this repo |
| Batch concurrency coordinator | 🟡 Functional | Semaphore logic is correct; popup bypasses it entirely |
| Explorer right-click integration | 🔴 Missing | No shell verb / COM / installer yet (expected — Phase 5) |
| Tests / CI / logging | 🔴 Missing | 0 test projects, 0 workflows, 0 logging, 0 `LICENSE` |
| Documentation | 🔴 Broken | `PHASE_2_SPEC.md` was 9,454 NUL bytes; README described a different product |

**Overall:** the *scaffolding* is genuinely good and above typical Phase 1 quality. The **product does not convert anything yet**, and the format matrix currently promises far more than the team has agreed to ship. Fix the four 🔴 items in §6 and Phase 2 can run cleanly; skip them and Phase 2 will fight the scaffolding.

---

## 2. The headline finding: there is no vertical slice yet

The stated plan is "get a real JPG-to-PNG conversion working first". That conversion is not in the repository.

Proof — `src/QuickConvert.Core/Services/MockConversionEngine.cs:90-120`:

```csharp
const int totalSteps = 50;          // simulated progress, 50 × 50 ms
...
await File.WriteAllTextAsync(destinationFilePath, dummyBanner, CancellationToken.None);
```

A request to convert `photo.jpg` → `photo.png` produces a **plain-text file with a `.png` extension** containing a banner (`"QuickConvert (Phase 1 Mock Engine Output)"`). It opens in no image viewer. The same is true for `.mp3`, `.mp4`, `.pdf`.

The only engine assembly that could do real work, `MediaEngineStub.cs:35` and `DocsEngineStub.cs:36`, throws:

```csharp
throw new NotImplementedException("MediaEngine is slated for implementation in Phase 3.");
```

There is **no image engine project at all** — no `QuickConvert.Engines.Images`, no `System.Drawing`, no `SkiaSharp`, no `ImageSharp` reference anywhere in the solution.

**Consequence for planning:** Phase 1 delivered *plumbing*, not the vertical slice. Phase 2 must begin with the image engine, not with UI polish. Please correct the internal status before the next relay hand-off, otherwise the UI designer will build screens on top of a mock that silently "succeeds" on every format pair.

---

## 3. Findings by severity

### P0 — blocks the vertical slice

**P0-1 · No image engine exists.**
The agreed slice (JPG→PNG, plus PNG/WEBP/BMP/TIFF) has no implementation, no project, no package. See §2.
*Fix:* add `src/QuickConvert.Engines.Images` and one real engine. Recommended library: **SkiaSharp** (MIT, self-contained native asset, encodes JPEG/PNG/WEBP/AVIF, decodes HEIC on Windows builds). `System.Drawing.Common` is the zero-extra-native-deps alternative but is Windows-only and GDI+-quality — fine for v1, worse for WEBP/AVIF.

**P0-2 · A single `IConversionEngine` singleton cannot serve four engines.**
`App.xaml.cs:67` registers exactly one engine:

```csharp
services.AddSingleton<IConversionEngine, MockConversionEngine>();
```

`IConversionEngine` is resolved as a *single* instance everywhere (`QuickPopupViewModel` ctor, `BatchConversionCoordinator` ctor default). As soon as the image, media and docs engines all exist, this registration has no way to express "route `.jpg`→`.png` to Images, `.mp4`→`.mp3` to Media". DI will happily resolve the last registration and every other engine becomes unreachable.
*Fix before writing engine #2:* introduce an `IConversionEngineRouter` (or `IEnumerable<IConversionEngine>` + a selector), and have the coordinator/VM ask the router for `(sourceExt, targetExt) → engine?`. This is a ~60-line change now and a multi-day refactor later.

**P0-3 · `CanConvert` is dead code — capability is never validated.**
`grep -rn "CanConvert"` finds only the interface declaration (`IConversionEngine.cs:23`) and three implementations. **Nothing ever calls it.** Combined with P0-2 and P1-1, the UI can start a conversion for any advertised pair, and the user only learns it is impossible when the engine throws (or, worse, when the mock "succeeds" and writes garbage).

### P1 — user-visible defects

**P1-1 · The format registry promises formats the product will never support.**
`FormatRegistry` declares 64 entries / 60 unique extensions across 8 categories, with targets that no planned engine can honour: `.psd`, `.cr2`, `.nef` (needs GIMP/LibRaw), `.heic`/`.avif` (decoder-dependent), `.svg` **as an output** of a raster image, `.mobi`/`.azw3`/`.fb2` (Calibre), `.woff`/`.eot` (needs a font compiler), `.m4a`/`.flac` from *any* video (FFmpeg, yes — but not before Phase 3), plus 12 spreadsheet cells that imply LibreOffice.

The agreed scope is ~25 essential formats. The registry advertises 2.5× that and is the source of truth for every combo box in the UI.
*Fix:* replace the hand-written per-category lists with a single explicit matrix (`source → [targets]` or a `ConversionMatrix` table), and **filter it by the engines that report `CanConvert == true` at startup**. Two sources of truth will drift again within two relays.

**P1-2 · Mixed selections fall back to an incompatible conversion.**
`QuickPopupViewModel.LoadFiles` (`:125-131`): if `GetCommonTargetFormats` is empty (e.g. 9 JPGs + 1 ZIP selected), the VM silently falls back to *the first file's* target list:

```csharp
var targets = FormatRegistry.GetSupportedTargetFormats(firstExt);
```

The user then sees "Convert 10 files to .PNG" and 9 of them work. The intersection being empty is correct information that must be surfaced ("No common target — convert images only / open dashboard"), not hidden.

**P1-3 · Popup conversions bypass the coordinator (serial, invisible to the dashboard).**
`QuickPopupViewModel` receives `BatchConversionCoordinator` in its constructor (`:61`) and **never uses it** (`_coordinator` is only assigned, `:64`). Instead `ConvertNowAsync` (`:152-219`) loops `await _engine.ConvertAsync(...)` one file at a time. Consequences:
- popup conversions are strictly serial, ignoring `SemaphoreSlim` concurrency and `ProcessorCount/2`;
- they never appear in `Jobs`, so dashboard counters and overall progress ignore them;
- there are **two parallel conversion paths** to maintain and test.

**P1-4 · Right-click silently does nothing when the IPC handshake fails.**
`SingleInstancePipeService.cs:111-160`. If the primary instance exists but its pipe server is not yet listening (startup race, or the primary is hung), `client.ConnectAsync` throws, the `catch` runs `Environment.Exit(0)`, and **the user's file list is discarded with zero feedback** — no popup, no tray balloon, no log. From the user's point of view the context-menu item is broken.
*Fix:* retry with back-off (e.g. 5 × 60 ms), and on final failure show a window/balloon ("QuickConvert is starting, please try again") instead of exiting silently. Also stop calling `Environment.Exit` from a **library** method — return a result and let `App` decide.

**P1-5 · Popup is placed in the wrong spot on scaled or multi-monitor desktops.**
`QuickPopupWindow.PositionNearCursor` (`:24-51`) mixes coordinate spaces: `GetCursorPos` returns **physical pixels**, `SystemParameters.PrimaryScreenWidth/Height` return **device-independent units**. On a 150 % display the popup lands ~1.5× too far right/down; on a secondary or left-hand monitor (`Left = -1920`) the `if (targetLeft < 20)` clamp throws it onto the primary screen.
*Fix:* per-monitor work area via `MonitorFromPoint` + `GetDpiForWindow`, or convert the point with `VisualTreeHelper.GetDpi` / `PresentationSource.FromVisual`. Cheap, high perceived-quality win.

**P1-6 · No error handling at app level, no logging anywhere.**
`OnStartup` is `async void` (`App.xaml.cs:24`) with no `try/catch` and no `DispatcherUnhandledException` / `AppDomain.UnhandledException` hook. An exception during startup or in a command disappears into a silent process exit. The `catch { }` blocks (`SingleInstancePipeService.cs:197,249,292`; `OpenOutputFolder` in both VMs) discard the only diagnostic evidence the team will ever have.
*Fix:* add a minimal `ILogger`-style sink (a rolling file in `%LOCALAPPDATA%\QuickConvert\logs`) plus a global handler that logs and shows a message. This is the single highest-leverage 100 lines in the project for a 4-person relay team, because every subsequent bug report will otherwise be "it doesn't work".

**P1-7 · `PHASE_2_SPEC.md` was unrecoverable.**
The file committed in `c9e1a32` is 9,454 bytes of `\0` (verified: `tr -d '\000' | wc -c` → `0`). It cannot be restored from Git history (single commit). Rewritten as part of this review — see the file.

### P2 — correctness / maintainability

**P2-1 · Duplicate-insert guard misses `Queued` jobs.**
`BatchConversionCoordinator.cs:73-79` de-duplicated on `Pending || Converting` only. A job already `Queued` (i.e. `StartAllAsync` has begun) passes the filter, so the same file can be enqueued twice mid-batch and converted twice.

**P2-2 · Default target format is non-deterministic for multi-file selections.**
`GetCommonTargetFormats` (`FormatRegistry.cs:214-243`) returns a `HashSet<string>` after `IntersectWith`; enumeration order is an implementation detail. `AvailableTargetFormats[0]` then becomes the pre-selected format, so the same 10-file selection can default to `.webp` on one launch and `.tiff` on the next.
*Fix applied in this branch:* preserve the first file's order and filter by the intersection.

**P2-3 · No pre-flight validation of output paths.**
Nothing checks write permission, free space, path length (MAX_PATH legacy), or that `OutputDirectory` exists before the batch starts. In the mock this is invisible because the write is 600 bytes; with FFmpeg writing 2 GB it becomes a mid-batch failure. `MockConversionEngine` does `Directory.CreateDirectory(outDir)` deep inside the job.

**P2-4 · Per-job `TargetExtension` vs. global `ConversionOptions.TargetExtension` is ambiguous.**
`MainDashboardViewModel.StartQueueAsync` always passes `TargetExtension = SelectedGlobalFormat`, which may be `""`. `ProcessJobAsync` then prefers the per-job value only when the global is blank. There is no validation that a per-job target is legal for that job's category — a `.mp4` row can be started with `.png` in the Target column.

**P2-5 · `AddFiles` overload shadows the generated command.**
`MainDashboardViewModel` has both `public void AddFiles(IEnumerable<string>)` and `[RelayCommand] public void AddFiles()`. This compiles only because the MVVM Toolkit emits a `new Action(AddFiles)` cast; it is a CS0121 trap for anyone who edits it. Rename the non-command overload to `AddFilePaths`.

**P2-6 · `Core` is `net9.0` but P/Invokes `user32.dll`.**
`SingleInstancePipeService.GetCursorPosition()` lives in the "pure" Core library and calls `GetCursorPos`. This makes Core impossible to unit-test on a Linux CI agent (DllNotFoundException at first touch) and misrepresents Core as platform-neutral.
*Fix:* move IPC + Win32 interop to a `QuickConvert.Platform.Windows` (`net9.0-windows`) project; keep Core to models, interfaces, registry and the coordinator.

**P2-7 · `MockConversionEngine` is still the default everywhere.**
Both the coordinator's optional-ctor default (`engine ?? new MockConversionEngine()`) and the DI registration point at the mock. If the mock is ever wired by accident in a release build, users get text files named `.mp4`. Consider compiling it under `#if DEBUG` or renaming it `FakeConversionEngine` and registering it only in a `DesignTime`/`Debug` DI branch.

**P2-8 · Unused / unverified dependencies.**
`Hardcodet.NotifyIcon.Wpf 2.0.1` is referenced (`QuickConvert.UI.csproj:11`) and **never used** — no `TaskbarIcon` anywhere. Either implement the tray icon in Phase 2 or drop the reference.
`Microsoft.Extensions.DependencyInjection 10.0.14` in a .NET 9 solution is a version mismatch (a .NET 10 package band resolving `netstandard2.0` assets on `net9.0`). Pin the 9.0.x band for predictability.
**WPF-UI 4.3.0 [verify-on-build]:** `App.xaml` merges `pack://application:,,,/Wpf.Ui;component/Resources/Theme/Light.xaml` and `.../Resources/Wpf.Ui.xaml`. That is the WPF-UI 2.x/3.x convention; 4.x moved to `ui:ThemesDictionary Theme="…"` + `ui:ControlsDictionary`. If the app starts, the paths are still valid in 4.3.0 — if it throws on startup, this is the first line to check. Also `Theme="Light"` is hardcoded, so the "Dark/Light modes" bullet in the README is not implemented.

### P3 — polish / future work

- `FormatRegistry.CustomTargetMap` (`:12`) is declared and read (`:116`) but never populated — dead extension point.
- `.gif` appears in both `ImageFormats` and `VideoFormats`; the last writer wins (`Video`), so GIFs get video + audio-extraction targets and never image targets. Deliberate? Document it.
- `ConversionJob` property-changed handlers registered in `EnqueueFiles` are never unsubscribed; jobs removed via `RemoveJob`/`ClearCompleted` keep a closure chain to the coordinator alive.
- `OverallProgress` averages over *all* jobs including `Pending`, so adding 50 files to a running batch visibly drops the bar.
- `QuickPopupViewModel` progress maths (`:188-189`) uses `completedCount`, which does not advance on failures, so a failed file makes the bar jump on the next file.
- Localised/emoji button glyphs (`📁`, `🗑`, `➡`) will render as boxes on some Windows 10 configurations; prefer Segoe Fluent Icons.
- `QuickPopupWindow` has `ShowInTaskbar="True"` and `Topmost="True"`; a context-menu popup usually wants neither.
- Solution folders: add `tests` and `docs` solution folders; consider dropping x86 from the configuration matrix (shell extensions are x64-only in practice).
- Git history is a single squash commit — the "Phase Relay" naming in the README cannot be verified from history, and blame/bisect are useless for the team. Recommend one PR per phase task with the task id in the subject.

---

## 4. What the 25-format scope should look like in code

The registry and the scope have diverged. Suggested single source of truth (`ConversionMatrix`), which also gives `CanConvert` something to answer:

| Category | Sources | Targets |
|---|---|---|
| Image | jpg, jpeg, png, webp, bmp, tiff, gif, heic*, avif* | jpg, png, webp, bmp, tiff, gif, pdf |
| Audio | mp3, wav, flac, aac, ogg, m4a, wma | mp3, wav, flac, aac, ogg, m4a |
| Video | mp4, mkv, avi, mov, webm, wmv | mp4, mkv, webm, mov, gif, **+ audio extraction** |
| Document | txt, md, csv, json, docx, html, rtf, odt, pdf* | txt, md, html, docx, pdf, csv |
| Archive | zip, 7z, tar, gz, **rar (extract only)** | zip, 7z, tar, gz |
| E-book | epub, mobi | epub, pdf*, txt |

`*` = only if the chosen library can actually do it; verify before advertising. **`.rar` must appear as a source and never as a target** (already correct in code — keep it that way and add a unit test that asserts it, since it is a licence obligation, not a preference).

---

## 5. "$0 cost" also means licence cost — check these before Phase 3

The product is free *and* redistributable-only-if-compliant. Binaries under `tools/` are currently empty `.gitkeep`s, so nothing is at risk yet, but the plan needs a decision:

| Tool | Licence | Consequence |
|---|---|---|
| FFmpeg | LGPL by default, **GPL if built with `--enable-gpl`** | `libx264` (the usual H.264/MP4 encoder) is **GPL**. Shipping it means the whole distributed bundle is GPL. Options: ship an LGPL build and use `openh264`, or use Windows Media Foundation's H.264 encoder, or accept GPL and publish the notice + source offer. |
| 7-Zip / `7z.dll` | LGPL + **unRAR restriction** | Allowed: RAR *extraction* only. Must not be used to build a RAR creator — matches product scope. Ship the licence text. |
| Pandoc | GPL | Bundling it makes the document feature GPL-encumbered. Consider calling a user-installed Pandoc, or use the GPL-compatible route consciously. |
| ImageSharp | Six Labors Split Licence | **Not** "free for any use" — free only under revenue/OSS conditions. Prefer **SkiaSharp (MIT)**. |
| PdfSharp / PdfSharpCore | MIT | Safe for image→PDF and text→PDF. |
| QuestPDF / iText | Community / AGPL | Both have real strings; prefer PdfSharp for v1. |
| SharpCompress | MIT, pure managed | Removes the need to ship `7z.exe` for zip/tar/gz/rar-extract; only 7z *creation* needs the external binary. |

Add a `THIRD-PARTY-NOTICES.md` in Phase 3 when the first binary lands, and a test that fails if `tools/` is populated without a notice entry.

---

## 6. Recommended Phase 2 — ordered, with a definition of done

The order matters: **capability before UI, one engine before four.**

1. **Decide the matrix (§4).** Delete the 30+ extensions nobody will implement. 1–2 h, unblocks everything.
2. **Add `IConversionEngineRouter`** (P0-2) and route through it in the coordinator + popup. ~60 lines.
3. **Ship the image engine** (P0-1) as `QuickConvert.Engines.Images` with SkiaSharp: `jpg ↔ png ↔ webp ↔ bmp ↔ tiff ↔ gif → pdf`. Real progress (decode → encode), real cancellation, real `CanConvert`.
4. **Wire validation end-to-end** (P0-3): registry targets ∩ router capability; `CanConvert == false` must be impossible to reach from the UI.
5. **Harden IPC** (P1-4): retry, no `Environment.Exit` in the library, add `PipeOptions.CurrentUserOnly` on the server, and a user-visible failure path.
6. **Add logging + global exception handling** (P1-6).
7. **Fix the popup path** (P1-3): route through `BatchConversionCoordinator` so there is exactly one execution path.
8. **Fix popup placement** (P1-5) and the mixed-selection fallback (P1-2).
9. **Then** UI polish (Fluent theming, dark/light, tray) and only then Phase 3.

**Definition of done for the vertical slice** — a reviewer must be able to tick all of these:

- [ ] Convert a real 4000×3000 JPG from Explorer to PNG; the output opens in Photos and is pixel-identical in dimensions.
- [ ] Convert to WEBP and confirm the file is a valid WEBP (magic bytes `RIFF....WEBP`), not a renamed JPG.
- [ ] EXIF orientation is respected (photo taken on a phone is not rotated).
- [ ] Selecting 10 JPGs produces 10 files, in parallel, with no duplicated names.
- [ ] Cancelling mid-batch leaves no zero-byte or partial output file (§P2-3).
- [ ] Converting a `.zip` → `.png` is rejected in the UI, not by an exception.
- [ ] RAR appears as a source only.
- [ ] `dotnet test` runs the matrix + naming + cancellation tests green on a Windows agent.

### Suggested 4-person relay split

| Owner | Owns | Must not touch |
|---|---|---|
| A (Core) | Matrix, router, coordinator fixes, tests project | XAML |
| B (Image engine) | `Engines.Images` + its tests; naming/overwrite/EXIF policy | Core interfaces |
| C (UI) | Single execution path, popup fixes, DPI/placement, theming, tray | engine internals |
| D (Platform/Packaging) | IPC hardening, logging, CI, `Directory.Build.props`, Phase 5 shell verb spike, licence notices | `ConversionMatrix` content |

Contract freeze: `IConversionEngine`, `ConversionJob`, `ConversionOptions`, `ConversionResult`, `IConversionEngineRouter`. Any change to these is a PR that the other three must approve.

---

## 7. Changes made in this review branch

| File | Change |
|---|---|
| `docs/AUDIT_PHASE_1.md` | This audit (new). |
| `PHASE_2_SPEC.md` | **Rebuilt.** The committed file was 9,454 NUL bytes and unrecoverable. |
| `README.md` | Corrected to the real 25-format scope, real status (no conversion engine yet), real file tree, RAR extract-only, audit link. |
| `.github/workflows/ci.yml` | Windows build gate (new) — `dotnet build` + conditional `dotnet test`. |
| `src/QuickConvert.Core/Services/FormatRegistry.cs` | `GetCommonTargetFormats` now deterministic (P2-2). |
| `src/QuickConvert.Core/Services/BatchConversionCoordinator.cs` | Duplicate-enqueue guard covers `Queued` (P2-1). |
| `src/QuickConvert.Core/Ipc/SingleInstancePipeService.cs` | Pipe server is same-user only (hardening for P1-4). |

Nothing in this branch changes the format matrix, the engine contracts or the UI — those need the team's decisions (§4, §6).

---

## 8. Appendix — snippets worth stealing

**Engine router (P0-2):**

```csharp
public interface IConversionEngineRouter
{
    IConversionEngine? Resolve(string sourceExt, string targetExt);
    IReadOnlyList<string> GetTargetsFor(string sourceExt); // matrix ∩ capability
}

public sealed class ConversionEngineRouter(IEnumerable<IConversionEngine> engines) : IConversionEngineRouter
{
    public IConversionEngine? Resolve(string sourceExt, string targetExt) =>
        engines.FirstOrDefault(e => e.CanConvert(sourceExt, targetExt));

    public IReadOnlyList<string> GetTargetsFor(string sourceExt) =>
        ConversionMatrix.GetTargets(sourceExt)
            .Where(t => Resolve(sourceExt, t) is not null)
            .ToList();
}
```

**Silent-exit fix (P1-4):** replace the `catch { Environment.Exit(0); }` with a bounded retry and a result the caller can act on:

```csharp
public async Task<bool> TrySendAsync(IReadOnlyList<string> files, CancellationToken ct)
{
    for (var attempt = 0; attempt < 5; attempt++)
    {
        try { /* connect + write */ return true; }
        catch (IOException) { await Task.Delay(60 * (attempt + 1), ct); }
        catch (TimeoutException) { await Task.Delay(60 * (attempt + 1), ct); }
    }
    return false; // App decides: show a balloon, keep the window open, or log.
}
```

**Unique output naming (do it once, in Core, not per engine):**

```csharp
// photo.png, photo (1).png, photo (2).png  — never photo_1.png mixed with photo (1).png
while (File.Exists(candidate)) candidate = Path.Combine(dir, $"{baseName} ({++n}){ext}");
```
