# Phase 3 Specification: Media Engine (Audio and Video)

**Status:** approved for implementation  
**Scope:** Phase 3 only. Product-level planning, decision gates, and risks are in `ROADMAP.md`.  
**Prerequisite:** Phase 1 and Phase 2 merged into `main` (through commit `63d9347`).

---

## 1. Objective

Phase 2 established the core capability framework (`ConversionMatrix`, `IConversionEngineRouter`), delivered
the image conversion engine (`MagickImageConversionEngine`), implemented safe output promotion, and made batch
concurrency limits configurable. All media formats (video containers and audio streams) currently map to stubs
and cannot convert.

Phase 3 implements production media processing using FFmpeg as an isolated subprocess:

1. A secure, non-shelling subprocess execution runner (`FfmpegProcessRunner`) with process tree termination,
   circular stderr diagnostics, and idle timeout protection.
2. A three-tier tool discovery mechanism prioritizing isolated and local distributions, paired with a cached
   hardware encoder probe (NVENC, QSV, AMF) verified via single-frame test encodes, backed by automatic software
   encoding fallbacks.
3. The media conversion engine (`FfmpegConversionEngine`) with pure argument construction (`FfmpegArgumentBuilder`),
   duration parsing from `ffprobe`, real-time progress parsing over `-progress pipe:1`, graceful cancellation, and
   integration with atomic output promotion (`OutputPromotion`).
4. Dependency injection wiring in the desktop shell with user-friendly missing-tool diagnostics that guide users
   without modal dialog flooding.
5. Resolution of Decision D1 (GPL build selection with `libx264` support), establishing complete licensing isolation
   and attribution in `THIRD-PARTY-NOTICES.md`.

**Completion Criterion:** A reviewer can convert between all matrix-supported video and audio pairs on Windows,
observe accurate progress within 5 percentage points, cancel active transcodes without orphaned `ffmpeg.exe`
processes or partial files, and experience graceful degradation if FFmpeg is uninstalled or if a hardware
encoder is absent.

---

## 2. Constraints

1. **Subprocess Isolation:** Commands are executed strictly through `ProcessStartInfo.ArgumentList` with
   `UseShellExecute = false` and `CreateNoWindow = true`. Command string concatenation and shell invocations
   (`cmd.exe`, `powershell.exe`) are strictly prohibited.
2. **Offline Runtime:** The application performs zero network requests at runtime. All tools must be discovered
   locally on the machine. Tool download scripts (`tools/Get-Ffmpeg.ps1`) are restricted to developer and CI
   environments and are never invoked by the application binary.
3. **Subprocess Resilience:** A hung or deadlocked encode must never block the batch coordinator indefinitely. An
   inactivity timeout triggers process tree cancellation after a configured idle duration.
4. **Clean Abort & Atomic Promotion:** Output files are written to unique temporary paths in the target directory
   first. Finished conversions are atomically promoted using `OutputPromotion.Promote(..., overwrite: false)`
   unless overwrite was explicitly requested. Cancelled or failed jobs purge only temporary files and leave
   existing destination files untouched.
5. **Architectural Gating:** The engine router vets every format pair against `ConversionMatrix` before delegating
   to `FfmpegConversionEngine`. Unsupported format pairs or absent binaries report cleanly via `CanConvert` and
   never trigger uncaught exceptions.
6. **Licensing Isolation:** Quick-Convert source code remains under the MIT license. FFmpeg is distributed as a
   separate binary executable subject to GPL v3 obligations. Third-party notices and source access directions
   must accompany the implementation.

---

## 3. Deliverable 1: Subprocess Runner

**Target Project:** `QuickConvert.Engines.Media` (`net9.0-windows`)

Implement an internal, testable `FfmpegProcessRunner` governed by `IFfmpegProcessRunner`:

```csharp
namespace QuickConvert.Engines.Media;

public interface IFfmpegProcessRunner
{
    Task<int> RunAsync(
        string executablePath,
        IReadOnlyList<string> arguments,
        string workingDirectory,
        Action<string>? onStdoutLine,
        IProgress<string>? onStderrLine,
        CancellationToken ct,
        TimeSpan idleTimeout);
}
```

### Key Requirements

1. **Process Configuration:**
   - Standard flags prepended to all execution arguments:
     `-hide_banner -nostdin -y -nostats -loglevel error`
   - Redirection enabled for standard output and standard error; standard input closed (`-nostdin`).
2. **Stderr Buffer:** Maintain a circular buffer of the most recent 50 stderr lines. When the process exits with
   a non-zero return code, format these captured lines into the error description.
3. **Process Tree Termination:**
   - On cancellation or timeout, call `process.Kill(entireProcessTree: true)`.
   - Await process exit up to 5 seconds. If the process does not terminate within the grace period, log a warning
     via `QuickConvertLogger`.
4. **Idle Timeout Watchdog:** Track timestamps of received stdout/stderr output lines. If no output is observed
   for `idleTimeout` (default 30 seconds for transcoding, 10 seconds for probing), abort the process tree with a
   `TimeoutException`.
5. **Testability:** All engine logic depends on `IFfmpegProcessRunner`, allowing `FakeFfmpegProcessRunner` to mock
   exit codes, standard output progress streams, and timeout behaviors in automated test suites without requiring
   an installed binary.

---

## 4. Deliverable 2: Tool Discovery and Capability Probe

### Tool Locator (`FfmpegLocator`)

Resolves binary locations for `ffmpeg.exe` and `ffprobe.exe` in the following strict search hierarchy:

1. User Local AppData pinned path:
   `%LOCALAPPDATA%\QuickConvert\tools\ffmpeg\<version>\bin\ffmpeg.exe`
2. Application directory bundle:
   `Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "tools", "ffmpeg", "bin", "ffmpeg.exe")`
3. System `PATH` environment variable lookup.

### Developer Acquisition Script (`tools/Get-Ffmpeg.ps1`)

- Downloads the pinned Gyan.dev FFmpeg 9.0.2 essentials release package.
- Verifies SHA-256 hash `60f467265b1e312373dbcd92200c2618a74850f98d3d078e94296bb3fa2047ba`.
- Extracts binaries (`ffmpeg.exe`, `ffprobe.exe`) into `tools/ffmpeg/bin/`.
- Restricted to developer setup and CI automation; excluded from Git via `.gitignore`.

### Capability & Hardware Encoder Probe (`FfmpegProbeService`)

1. Executes once per process lifetime and caches results.
2. Probes FFmpeg version string and registered encoders (`ffmpeg -encoders`).
3. Evaluates hardware acceleration codecs:
   - NVIDIA NVENC: `h264_nvenc`, `hevc_nvenc`
   - Intel QuickSync: `h264_qsv`
   - AMD AMF: `h264_amf`
4. Validates each candidate hardware encoder using a 1-frame dummy transcode:
   `-f lavfi -i color=c=black:s=64x64:d=0.04 -c:v <encoder> -f null -`
5. If the trial transcode fails (e.g. driver listed but GPU incompatible or overloaded), the encoder is flagged
   unavailable.
6. **Runtime Fallback:** If an active transcode fails using a hardware encoder, the engine logs the fallback via
   `QuickConvertLogger.Warn` and immediately retries the job using the baseline software encoder (`libx264`).
7. **Missing Tool Handling:** If neither `ffmpeg.exe` nor `ffprobe.exe` is found:
   - `CanConvert` returns `false` for all format pairs.
   - Media targets do not appear in user interface dropdowns.
   - If invoked directly, `ConvertAsync` returns a non-exceptional `ConversionResult(false, ..., "FFmpeg is required for audio and video conversions. Run tools/Get-Ffmpeg.ps1 to install it.")`.

---

## 5. Deliverable 3: Media Engine and Argument Construction

### Argument Builder (`FfmpegArgumentBuilder`)

A pure functional builder responsible for translating format conversion requests into command-line arguments:

- **MP4 to MP3:** `-vn -c:a libmp3lame -b:a 192k`
- **Video to WAV:** `-vn -c:a pcm_s16le`
- **Video to M4A:** `-vn -c:a aac -b:a 192k`
- **MP4 to WEBM:** `-c:v libvpx-vp9 -crf 32 -b:v 0 -c:a libopus`
- **WEBM to MP4:** `-c:v libx264 -crf 20 -preset medium -c:a aac` (or verified `h264_nvenc`/`h264_qsv` with equivalent rate control)
- **Container Copy to MKV:** stream copy (`-c copy`) when codecs are Matroska-compliant; transcode when incompatible
- **Audio to Audio:** stream copy when codecs match; transcode at 192k bitrate when formats differ (MP3 `libmp3lame`, AAC `aac`, OGG `libvorbis`, FLAC `flac`, WAV `pcm_s16le`)
- **Video to GIF:** Two-pass palette generation for high visual fidelity:
  `-filter_complex "[0:v] fps=15,scale=w=480:h=-1:flags=lanczos,split [a][b];[a] palettegen [p];[b][p] paletteuse"`

### Duration & Progress Tracking

1. Before starting a transcode, `FfmpegConversionEngine` queries stream duration using `ffprobe`:
   `ffprobe -v error -show_entries format=duration -of default=noprint_wrappers=1:nokey=1 "<input>"`
2. The transcode argument list includes `-progress pipe:1`.
3. Standard output lines are parsed by `FfmpegProgressParser`:
   - Extracts `out_time_us` (microseconds) with fallback to `out_time_ms`.
   - Computes current position relative to total duration.
   - Normalizes progress percentage into the range `0.0` to `95.0`.
   - If duration cannot be parsed, progress reports are suppressed.
   - Progress reaches `100.0` strictly after output promotion completes successfully.

### Output Promotion Integration

- Output is rendered to a temporary file (`.tmp_<guid><ext>`) inside the designated output folder.
- On success, `OutputPromotion.Promote(tempPath, outDir, baseName, targetExt, firstChoice, options.OverwriteExisting)`
  atomically renames the file or selects `baseName (n).ext` if a race occurred.
- On cancellation or error, only the temporary `.tmp_*` file is deleted. Existing destination files remain untouched.

---

## 6. Deliverable 4: Shell Integration and Dependency Injection

1. Register `FfmpegConversionEngine` in `src/QuickConvert.UI/App.xaml.cs`:
   ```csharp
   services.AddSingleton<IConversionEngine, FfmpegConversionEngine>();
   ```
2. ViewModels continue to reference only `IConversionEngineRouter` and `BatchConversionCoordinator`.
3. When media files (`.mp4`, `.mov`, `.mp3`, etc.) are dropped or loaded from Explorer:
   - If FFmpeg is installed, the router resolves valid target formats from `ConversionMatrix`.
   - If FFmpeg is absent, the router returns empty target lists, displaying "No compatible conversion available"
     with diagnostic log entries.

---

## 7. Deliverable 5: Decision Gate D1 and Notices

### Decision D1: FFmpeg Build Configuration

- **Option A (Selected):** Pinned GPL v3 build (Gyan.dev / BtbN). Includes `libx264`, `libx265`, and `libvpx-vp9`.
  Provides production H.264/H.265 encoding support on Windows without licensing ambiguities.
- **Option B (Rejected):** LGPL build. Excludes `libx264` and `libx265`, relying solely on OpenH264 or native Media Foundation,
  which lacks full encoding profile support and multi-platform consistency.
- **Licensing Compliance Architecture:**
  - Quick-Convert executes `ffmpeg.exe` and `ffprobe.exe` strictly as separate external command-line utilities.
  - Data transfer occurs over standard operating system pipes (`stdout`, `stderr`).
  - No static or dynamic in-process linking occurs.
  - Quick-Convert source code remains under the permissive MIT license.
  - `THIRD-PARTY-NOTICES.md` is updated with full GPL v3 notices, build flags, upstream repository links, and instructions
    for obtaining the exact corresponding source code.

---

## 8. Testing Strategy

1. **Deterministic Test Suite (Zero External Dependencies):**
   - All tests in `tests/QuickConvert.Engines.Media.Tests/` run and pass without requiring `ffmpeg.exe` or `ffprobe.exe`.
   - Uses `FakeFfmpegProcessRunner` with canned responses, mocked progress lines, and configurable return codes.
2. **Unit Test Scenarios:**
   - **Argument Builder:** Verifies exact parameter lists for all required conversion pairs (MP4->MP3, Video->WAV,
     Video->M4A, MP4->WEBM, WEBM->MP4, Video->GIF, Audio->Audio).
   - **Progress Parser:** Validates monotonic sequence, microsecond conversions, clamped 0..95 range, and completion.
   - **Duration Parser:** Tests valid durations, scientific notation, and malformed probe outputs.
   - **Tool Locator:** Tests directory hierarchy search order and fallback to PATH.
   - **Hardware Encoder Gating:** Tests trial encode detection and fallback to software encoding.
   - **Cancellation Cleanup:** Verifies that cancelling a transcode terminates the process tree and purges only
     temporary files.
3. **Integration & Manual Acceptance Tests:**
   - Real transcode tests execute conditionally when FFmpeg is present on the development machine.
   - Live media acceptance runs verify MP4->MP3 audio extraction, round-trip video transcoding, and 30-second
     clip progress tracking.

---

## 9. Pull Request Cadence

Work is executed and merged in four sequential branches:

1. `phase3/media-runner`: Subprocess runner (`FfmpegProcessRunner`), tool locator (`FfmpegLocator`), capability probe
   (`FfmpegProbeService`), `Get-Ffmpeg.ps1`, shared `OutputPromotion` accessibility, and initial unit test suite.
2. `phase3/media-engine`: Pure argument builder (`FfmpegArgumentBuilder`), progress parser (`FfmpegProgressParser`),
   full `FfmpegConversionEngine`, and transcode unit tests.
3. `phase3/media-wiring`: Dependency injection registration in `App.xaml.cs`, UI format resolution, and missing-tool
   user notifications.
4. `phase3/docs`: Decision D1 documentation, `THIRD-PARTY-NOTICES.md` update, `docs/PHASE_3_STATUS.md`, and
   corrections to `docs/PHASE_2_STATUS.md`.
