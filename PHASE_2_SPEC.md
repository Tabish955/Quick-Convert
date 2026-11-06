# Phase 2 Specification: Image Conversion and Launch Path

**Status:** in progress
**Scope:** Phase 2 only. Product-level planning, decision gates and risks are in `ROADMAP.md`.
**Prerequisite:** Phase 1, whose outstanding verification items are listed in `docs/PHASE_1_CLOSEOUT.md`.

---

## 1. Objective

Phase 1 delivered the application skeleton: single instance handling, inter-process communication, the
MVVM shell and the batch coordinator. All conversions currently run through `MockConversionEngine`, which
simulates progress and writes a text file with the requested extension. No real conversion exists.

Phase 2 delivers the first real conversion path and the supporting infrastructure that later engines will
use:

1. A single definition of which conversions are supported.
2. An engine selection layer that can serve several engines.
3. A working image engine.
4. Capability checking, so the interface never offers a conversion that will fail.
5. A launch path that reports failures instead of discarding the user's selection.
6. One conversion execution path shared by both windows.
7. A timeboxed investigation of the Explorer context menu.

Phase 2 does not include audio, video, document or archive conversion, the production context-menu
integration, or the installer. Those belong to Phases 3 to 5.

**Completion criterion:** a reviewer can select one or ten JPEG files in File Explorer, request PNG or
WEBP, receive valid images, cancel a batch without leaving partial files behind, and never see a format
offered that the application cannot produce.

---

## 2. Constraints

1. The supported format scope is the set agreed in `ROADMAP.md` section 2. It is not extended in Phase 2.
2. RAR may be used as an input format and must never be offered as an output format. This is a licence
   obligation arising from the unRAR restriction, and a unit test enforces it.
3. The application performs no network access at runtime and collects no telemetry.
4. All conversions are executed by `BatchConversionCoordinator`. No view model may call
   `IConversionEngine.ConvertAsync` directly.
5. The interface may only offer conversions that an engine reports as supported.
6. `FormatRegistry` is a lookup table and is not the definition of product scope. The definition is
   `ConversionMatrix`.

---

## 3. Deliverable 1: `ConversionMatrix`

**Owner:** Core. This work blocks every other deliverable.

Replace the per-category format lists in `FormatRegistry` with a single explicit table. Remove any format
that is not in the table.

```csharp
namespace QuickConvert.Core.Services;

public static class ConversionMatrix
{
    // Source extension to allowed target extensions.
    private static readonly Dictionary<string, string[]> Matrix = new(StringComparer.OrdinalIgnoreCase)
    {
        // Images
        [".jpg"]  = [".png", ".webp", ".bmp", ".tiff", ".gif", ".pdf"],
        [".jpeg"] = [".png", ".webp", ".bmp", ".tiff", ".gif", ".pdf"],
        [".png"]  = [".jpg", ".webp", ".bmp", ".tiff", ".gif", ".pdf"],
        [".webp"] = [".jpg", ".png", ".bmp", ".tiff", ".gif", ".pdf"],
        [".bmp"]  = [".jpg", ".png", ".webp", ".tiff", ".gif", ".pdf"],
        [".tiff"] = [".jpg", ".png", ".webp", ".bmp", ".gif", ".pdf"],
        [".gif"]  = [".jpg", ".png", ".webp", ".bmp", ".tiff", ".pdf"],

        // Audio (Phase 3)
        [".mp3"]  = [".wav", ".flac", ".aac", ".ogg", ".m4a"],
        [".wav"]  = [".mp3", ".flac", ".aac", ".ogg", ".m4a"],
        [".flac"] = [".mp3", ".wav", ".aac", ".ogg", ".m4a"],
        [".aac"]  = [".mp3", ".wav", ".flac", ".ogg", ".m4a"],
        [".ogg"]  = [".mp3", ".wav", ".flac", ".aac", ".m4a"],
        [".m4a"]  = [".mp3", ".wav", ".flac", ".aac", ".ogg"],
        [".wma"]  = [".mp3", ".wav", ".flac"],

        // Video (Phase 3)
        [".mp4"]  = [".mkv", ".webm", ".mov", ".gif", ".mp3", ".wav", ".m4a"],
        [".mkv"]  = [".mp4", ".webm", ".mov", ".gif", ".mp3", ".wav", ".m4a"],
        [".avi"]  = [".mp4", ".mkv", ".webm", ".mov", ".gif", ".mp3", ".wav"],
        [".mov"]  = [".mp4", ".mkv", ".webm", ".gif", ".mp3", ".wav"],
        [".webm"] = [".mp4", ".mkv", ".mov", ".gif", ".mp3", ".wav"],
        [".wmv"]  = [".mp4", ".mkv", ".gif", ".mp3", ".wav"],

        // Documents and spreadsheets (Phase 4)
        [".txt"]  = [".md", ".html", ".pdf", ".docx", ".csv"],
        [".md"]   = [".txt", ".html", ".pdf", ".docx"],
        [".html"] = [".txt", ".md", ".pdf", ".docx"],
        [".csv"]  = [".json", ".txt", ".xlsx", ".md"],
        [".json"] = [".csv", ".txt", ".yaml"],
        [".docx"] = [".pdf", ".txt", ".md", ".html", ".rtf", ".odt"],
        [".rtf"]  = [".txt", ".html", ".pdf", ".docx"],
        [".odt"]  = [".txt", ".html", ".pdf", ".docx"],
        [".pdf"]  = [".txt", ".png", ".jpg"],   // Text extraction or first page only.

        // Archives (Phase 4)
        [".zip"] = [".7z", ".tar", ".gz"],
        [".7z"]  = [".zip", ".tar"],
        [".tar"] = [".zip", ".7z", ".gz"],
        [".gz"]  = [".zip", ".7z", ".tar"],
        [".rar"] = [".zip", ".7z", ".tar"],     // Input only. Never an output format.
    };

    /// <summary>Extensions that must never be offered as an output format.</summary>
    public static IReadOnlySet<string> ForbiddenTargets { get; } =
        new HashSet<string>([".rar"], StringComparer.OrdinalIgnoreCase);

    public static bool IsKnownSource(string extension) => Matrix.ContainsKey(Normalize(extension));

    public static IReadOnlyList<string> GetTargets(string sourceExtension) =>
        Matrix.TryGetValue(Normalize(sourceExtension), out var targets) ? targets : [];

    public static bool IsAllowed(string sourceExtension, string targetExtension) =>
        GetTargets(sourceExtension).Contains(Normalize(targetExtension), StringComparer.OrdinalIgnoreCase);

    private static string Normalize(string? extension)
    {
        if (string.IsNullOrWhiteSpace(extension))
        {
            return string.Empty;
        }

        var trimmed = extension.Trim().ToLowerInvariant();
        return trimmed.StartsWith('.') ? trimmed : "." + trimmed;
    }
}
```

### Acceptance criteria

- [ ] `FormatRegistry` contains no hardcoded per-category format arrays and delegates to `ConversionMatrix`.
- [ ] A test asserts that no entry in `ForbiddenTargets` appears in any target list.
- [ ] Every source extension in the matrix resolves to a `FileCategory` other than `Unknown`.
- [ ] Removing an extension from the matrix removes it from every list in the interface, with no other
      code change.

---

## 4. Deliverable 2: `IConversionEngineRouter`

**Owner:** Core. This work is required before a second engine is added.

`IConversionEngine` is currently registered as a single implementation in dependency injection
(`App.xaml.cs:67`). With more than one engine, one implementation becomes unreachable. Introduce a router:

```csharp
public interface IConversionEngineRouter
{
    IConversionEngine? Resolve(string sourceExtension, string targetExtension);

    /// <summary>Formats the matrix allows and an engine can actually produce.</summary>
    IReadOnlyList<string> GetAvailableTargets(string sourceExtension);

    string GetEngineName(string sourceExtension, string targetExtension);
}
```

### Requirements

- Register the router as a singleton over `IEnumerable<IConversionEngine>`; register engines as multiple
  `IConversionEngine` implementations.
- `GetAvailableTargets` is the only source for format lists in view models. Nothing else reads
  `FormatRegistry` or `ConversionMatrix` to populate the interface.
- The coordinator and the popup call `Resolve`. A `null` result fails the job before it is enqueued, with a
  user-facing message such as "MP4 to PNG is not supported. Convert to MP3 or use the dashboard."

### Acceptance criteria

- [ ] Two engines can be registered at once, and each receives only the files it supports (verified with a
      test double).
- [ ] No call to `IConversionEngine.ConvertAsync` exists outside `BatchConversionCoordinator`.
- [ ] `IConversionEngine.CanConvert` is called during enqueue, so the method is no longer unused.

---

## 5. Deliverable 3: Image engine

**Owner:** Image engine. The interface work in section 7 depends on this deliverable.

Add a new project, `src/QuickConvert.Engines.Images`, implemented with Magick.NET
(`Magick.NET-Q8-AnyCPU`, Apache 2.0). One library covers raster formats, WEBP, AVIF, HEIC and ICO, which is
the agreed image scope. ImageSharp is not used because the Six Labors Split Licence is not unconditionally
free.

| Concern | Requirement |
|---|---|
| Library | `Magick.NET-Q8-AnyCPU`. Verify the Inno Setup size budget before Phase 5. |
| Input formats | jpg, jpeg, png, webp, bmp, tiff, gif (first frame), ico. HEIC and AVIF only after the decoder is confirmed on a clean machine. |
| Output formats | jpg, png, webp, bmp, tiff, gif (single frame), ico, and pdf through PdfSharp or the Magick.NET PDF writer. |
| Quality | `ConversionOptions.Quality` (1 to 100) maps to the JPEG and WebP encoder quality setting. |
| Target framework | `net9.0`, implements `IConversionEngine`. |
| Progress | Real values derived from the operation: 0 to 10 for decoding, 10 to 85 for encoding, 85 to 100 for writing and flush. |
| Cancellation | Check the token between decoding and encoding; delete any partial output before returning. |
| Orientation | Apply the EXIF orientation through `MagickImageInfo.Orientation` and `AutoOrient()`. A test covers this. |
| Output naming | `photo.png`, then `photo (1).png`, `photo (2).png`. Overwrite only when `OverwriteExisting` is set. |
| Failure handling | An unreadable input returns `ConversionResult` with `Success` false and a reason. `ConvertAsync` must not throw. |

### Acceptance criteria

- [ ] A 4000 by 3000 JPEG converts to PNG in under two seconds on a typical laptop, and the result opens in
      the Windows Photos application.
- [ ] The same source converted to WEBP produces a file beginning with the `RIFF` and `WEBP` markers.
- [ ] A photograph with EXIF orientation 6 is not rotated incorrectly after conversion.
- [ ] Cancelling during encoding leaves no partial file in the output folder.
- [ ] `MockConversionEngine` is not registered in any shipping configuration.

---

## 6. Deliverable 4: Launch path and diagnostics

**Owner:** Platform.

1. `SingleInstancePipeService`
   - Remove `Environment.Exit` from the library and return a result instead.
   - Retry a failed connection up to five times with a short back-off before reporting failure.
   - Create the server with `PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly`.
   - Keep the 150 ms debounce and the duplicate-path check.
2. `App.OnStartup`
   - Wrap startup in `try`/`catch`.
   - Handle `DispatcherUnhandledException` and `AppDomain.CurrentDomain.UnhandledException`.
   - Log the failure and show a message instead of terminating silently.
3. Logging
   - Rolling file at `%LOCALAPPDATA%\QuickConvert\logs\quickconvert-{date}.log`.
   - Seven-day retention.
   - File paths are recorded only at `Debug` level.
4. Failure feedback
   - If the handshake fails, the secondary instance shows a short message and exits. It must not exit
     without informing the user.

### Acceptance criteria

- [ ] Terminating the primary instance during the handshake, then selecting three files, produces a visible
      explanation and a log entry containing the reason.
- [ ] The `Global\` mutex path is verified with two user accounts on the same machine.
- [ ] An exception thrown in a command handler produces a log entry and a dialog rather than a vanished
      process.

---

## 7. Deliverable 5: Single execution path and popup behaviour

**Owner:** Interface.

- `QuickPopupViewModel` uses `BatchConversionCoordinator` instead of `IConversionEngine`, so concurrency,
  cancellation and progress are shared with the dashboard. The compact progress display is driven by
  coordinator events.
- A mixed selection with no common target format shows an explicit message and an "Open Dashboard" action.
  It must not fall back to the first file's target list.
- Popup placement converts the cursor position from physical pixels to the destination monitor's working
  area in device-independent units. Negative coordinates (monitors to the left of the primary) and
  per-monitor scaling are supported.
- Rename `AddFiles(IEnumerable<string>)` to `AddFilePaths` so it does not shadow the generated command.
- Either implement the theme switch and tray icon, or remove `Hardcodet.NotifyIcon.Wpf`, which is
  referenced but unused. Both are lower priority than the items above.

### Acceptance criteria

- [ ] The popup is positioned correctly at 100%, 150% and 200% scaling, on both primary and secondary
      monitors.
- [ ] A conversion started in the popup appears in the dashboard under the same job identifier.
- [ ] Selecting five JPEG files and one ZIP shows "no common target format" and does not queue the ZIP.

---

## 8. Deliverable 6: Context menu investigation

**Owner:** Platform. Timeboxed to half a day. The result resolves decision D2 in `ROADMAP.md`.

Add a per-user registry verb under `HKCU\Software\Classes\SystemFileAssociations\...\shell` that launches
the built executable, with `MultiSelectModel` set. This code is temporary and is not included in the
release.

The investigation must answer:

1. Where the entry appears on Windows 11 (primary menu or "Show more options").
2. The measured time from invocation to a visible popup with one file and with twenty files.
3. How many processes start for a twenty-file selection, and how many remain after the debounce.
4. Whether `MultiSelectModel` limits the selection as expected.

### Acceptance criteria

- [ ] The four questions above are answered with numbers in `docs/PHASE_1_CLOSEOUT.md` or a short note
      under `docs/`, and decision D2 is recorded.

---

## 9. Work breakdown

| # | Task | Owner | Depends on | Estimate |
|---|---|---|---|---|
| 1 | `ConversionMatrix`, file category mapping and tests | Core | none | 0.5 day |
| 2 | `IConversionEngineRouter`, dependency injection and test double | Core | 1 | 0.5 day |
| 3 | `Engines.Images` with Magick.NET: conversion, orientation, naming, cancellation | Image | 1 | 2 days |
| 4 | Route the coordinator and popup through the router; add pre-flight validation | Core and Interface | 2, 3 | 0.5 day |
| 5 | Launch path hardening, logging and global exception handling | Platform | none | 1 day |
| 6 | Popup unification, placement and mixed-selection handling | Interface | 4 | 1 day |
| 7 | Context menu investigation (timeboxed) | Platform | none | 0.5 day |
| 8 | Theming and tray icon | Interface | 6 | 0.5 day |

Tasks 1, 5 and 7 can run in parallel. Task 3 depends only on task 1. Task 8 starts only after tasks 4 and 6
are merged. Keeping CI green and adding a test with each change is an ongoing responsibility for everyone,
not a separate task.

---

## 10. Pull request checklist

Every Phase 2 pull request must satisfy the following.

- [ ] One concern per pull request, with the subject prefixed `core:`, `img:`, `ui:` or `platform:`.
- [ ] Any new package reference is accompanied by a licence, size and justification note.
- [ ] Changes to `IConversionEngine`, `ConversionJob`, `ConversionOptions` or `IConversionEngineRouter`
      have three approvals.
- [ ] `dotnet build QuickConvert.sln -c Release` succeeds and CI is green.
- [ ] Behaviour changes are covered by a test.
- [ ] No empty `catch` block without a log entry.
