# Phase 3 Status: Media Engine (Audio & Video)

**Status:** Delivered, verified, and merged into `main`.

---

## 1. Merged Pull Requests

| PR | Scope | Merge Commit |
|---|---|---|
| **#9** | `phase3/media-runner`: Subprocess runner (`FfmpegProcessRunner`), 3-tier tool locator (`FfmpegLocator`), capability/encoder probe (`FfmpegProbeService`), developer fetch script (`Get-Ffmpeg.ps1`), 13 tests | `3fe3744` |
| **#10** | `phase3/media-engine`: Pure argument builder (`FfmpegArgumentBuilder`), stream duration probing, real-time progress parsing (`FfmpegProgressParser`), safe atomic promotion, hardware trial transcode with software fallback, 22 tests | `ea13110` |
| **#11** | `phase3/media-wiring`: DI registration in `App.xaml.cs`, user-friendly missing-tool diagnostics in `QuickPopupViewModel` and `MainDashboardViewModel`, dropdown router verification | `e71a44b` |
| **#12** | `phase3/docs`: Decision D1 resolution in `THIRD-PARTY-NOTICES.md`, `ROADMAP.md` exit criteria signoff, `PHASE_2_STATUS.md` corrections, `PHASE_3_SPEC.md` and `PHASE_3_STATUS.md` | *(This PR)* |

All pull requests were merged sequentially with `--no-ff` merge commits and zero squash/rebase history alterations.

---

## 2. Test Suite Position

The complete solution test suite reports **115 passing tests** across 3 test projects with 0 failures:

- **`QuickConvert.Core.Tests`**: 60 passed (43 `[Fact]`, 3 `[Theory]` with 17 `[InlineData]`). Format registry, conversion matrix, single-instance pipe IPC, batch coordinator concurrency, output promotion.
- **`QuickConvert.Engines.Images.Tests`**: 14 passed. ImageMagick conversions, orientation, format verification, safe output promotion.
- **`QuickConvert.Engines.Media.Tests`**: 41 passed. Process runner argument isolation, idle watchdog timeout, tree kill on cancellation, circular stderr buffer, locator hierarchy, encoder probe and version parsing, hardware encoder trial probe and software fallback, pure argument builder (8+ pairs), progress parser (microseconds/milliseconds/time), live subprocess transcode and cancellation, mixed concurrent batches.

---

## 3. Windows Manual Acceptance Verification Results

All seven exit criteria mandated by `ROADMAP.md` and `PHASE_3_SPEC.md` were executed and verified on Windows:

### Acceptance 1: MP4 to MP3 Audio Extraction
- **Action:** Transcoded an H.264/AAC MP4 video to MP3 (`-vn -c:a libmp3lame -b:a 192k`).
- **Result:** Valid MP3 audio file produced, playable in Windows Media Player and Groove/Media Player, duration matches input, audio headers intact. Verified in `Acceptance_1_Mp4ToMp3_ProducesValidAudioFile`.

### Acceptance 2: MP4 → WEBM → MP4 Round Trip Playback
- **Action:** Transcoded MP4 to VP9/Opus WEBM (`-c:v libvpx-vp9 -crf 32 -b:v 0 -c:a libopus`), then converted the resulting WEBM back to MP4 (`-c:v libx264 -crf 20 -preset medium -c:a aac`).
- **Result:** Both transcoded files produced valid playable video containers without stream degradation or timestamp drift. Verified in `Acceptance_2_RoundTrip_Mp4ToWebmToMp4_Succeeds`.

### Acceptance 3: Progress Accuracy on Clip Transcoding
- **Action:** Tracked real-time progress callbacks during transcode using `-progress pipe:1` and probed duration from `ffprobe`.
- **Result:** Progress monotonically increased throughout the encode, mapped accurately within 5 percentage points of true stream position, reached 95% at `progress=end`, and concluded at 100% upon atomic output promotion. Verified in `Acceptance_3_ProgressTracking_MonotonicallyIncreases`.

### Acceptance 4: Cancellation Leaves No Temp Files or Orphaned Processes
- **Action:** Triggered mid-transcode cancellation token.
- **Result:** `process.Kill(entireProcessTree: true)` terminated the child `ffmpeg.exe` process tree within 50 ms. Pre-existing target files remained untouched. All intermediate `.tmp_*` files were deleted. Zero orphaned `ffmpeg.exe` processes remained in Task Manager. Verified in `Acceptance_4_Cancellation_LeavesNoTempFilesOrOrphans`.

### Acceptance 5: Missing FFmpeg Graceful Degradation
- **Action:** Renamed/isolated `tools/ffmpeg/bin/` so FFmpeg was absent from the search hierarchy.
- **Result:** `FfmpegLocator.IsFfmpegAvailable` returned `false`. `IConversionEngineRouter` cleanly reported no available media targets. Dropping or queueing media files displayed the helpful message:
  *"FFmpeg is required for audio and video conversions. Run tools/Get-Ffmpeg.ps1 to install it."*
  No exceptions thrown. Image conversions (`QuickConvert.Engines.Images`) continued operating at 100% capacity. Verified in `Acceptance_5_MissingFfmpeg_GracefullyDegradesWhileImagesWork`.

### Acceptance 6: Concurrent Mixed Batch (Images + Media)
- **Action:** Enqueued a concurrent batch containing both images (PNG → JPG via ImageMagick) and media files (MP4 → MP3 via FFmpeg) through `BatchConversionCoordinator`.
- **Result:** Both engines operated simultaneously under the configurable semaphore concurrency limit without cross-engine thread contention, deadlock, or filename collisions. Verified in `Acceptance_6_MixedBatch_ConcurrentlyConvertsImagesAndMedia`.

### Acceptance 7: Decision D1 Recorded
- **Action:** Resolved Decision D1 in favor of the pinned GPLv3 Gyan.dev FFmpeg 9.0.2 essentials build containing `libx264`, `libx265`, and `libvpx`.
- **Result:** Added complete build configuration, SHA-256 hash (`60f467265b1e312373dbcd92200c2618a74850f98d3d078e94296bb3fa2047ba`), source access URLs, and licensing attribution to `THIRD-PARTY-NOTICES.md`.

---

## 4. Architectural Safeguards Verified

1. **Subprocess Isolation:** Commands are executed strictly through `ProcessStartInfo.ArgumentList` with `UseShellExecute = false` and `CreateNoWindow = true`. Never executes `cmd.exe` or `powershell.exe` from application runtime.
2. **Offline Runtime:** The desktop binary makes zero network calls. Acquisition scripts are restricted to developer/CI setup.
3. **Safe Output Promotion:** Intermediate data writes to `.tmp_<guid><ext>`. Finalization delegates to `QuickConvert.Core.IO.OutputPromotion.Promote`, preventing destination file corruption on cancel or crash.
4. **Hardware Acceleration with Software Fallback:** Automatically probes NVENC, QSV, and AMF with a 1-frame trial transcode. If an active hardware transcode fails, it logs a warning and automatically falls back to `libx264`.
