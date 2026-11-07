# Phase 2 status

**Status:** delivered and merged into `main`. One verification item remains open: no GitHub Actions run
has completed yet.

## Merged work

| PR | Scope | Merge commit |
|---|---|---|
| #1 | `ConversionMatrix` replaces the hardcoded format lists in `FormatRegistry` | `3e4db52` |
| #2 | `IConversionEngineRouter`, capability gating in the coordinator, rejection result | `8802f32` |
| #3 | `QuickConvert.Engines.Images` on Magick.NET-Q8-AnyCPU 14.17.2 | `2be8117` |
| #4 | Popup and dashboard routed through the coordinator, dropdowns derived from the router | `540c8d8` |
| #5 | Named pipe hardening, rolling logger, global exception handlers, honest failure message | `be2a355` |
| #6 | D2 shell spike script, spike results, close-out update | `1bdd837` |

The six branches were stacked, so they were merged in order with merge commits. Nothing was squashed.

## Corrections applied during review

| Defect | Location | Correction |
|---|---|---|
| Failure dialog claimed files "will be picked up shortly" after they had been dropped, and would appear once per file | `App.xaml.cs` | Accurate message, plus a named mutex so only the first launcher in a batch reports |
| Engine resolved from the job target while the conversion ran with the batch target | `BatchConversionCoordinator.cs` | One `effectiveTarget` used by the gate, the resolve and the options |
| Cancelling an overwrite deleted the pre-existing destination file | `MagickImageConversionEngine.cs` | Write to a temporary file, promote on success, delete only the temporary file |
| Whole file buffered into a `byte[]` before decoding | `MagickImageConversionEngine.cs` | Decode from a `FileStream` with progress reported through a wrapper stream |
| Stale spec reference, incorrect PDF/Ghostscript comment, wrong shell file-count limits | `ConversionMatrix.cs`, `MagickImageConversionEngine.cs`, `D2_SPIKE_RESULTS.md`, `tools/spike/Add-QuickConvertVerb.ps1` | Corrected |

## Test position

The merged tree carries 55 Core test cases (38 `[Fact]`, 3 `[Theory]` with 17 `[InlineData]`) and 9 Images
facts. These figures match the counts reported for each merge. The RAR guard
(`NoSourceOffersRarAsATarget`) is intact and no format table lists RAR as a target.

## Open items

1. **CI has never run.** GitHub returns `HTTP 422: Actions has been disabled for this user`, which is an
   account-level restriction rather than a repository setting. See `docs/PHASE_1_CLOSEOUT.md` for the
   steps. Until one run is green, the test counts above rest on local runs only.

2. **Parallel conversion has no automated test.** `StartAllAsync` starts every job and awaits
   `Task.WhenAll`, limited by `SemaphoreSlim` with a degree of `Environment.ProcessorCount / 2`. The
   behaviour is real but untested, and on a two-core machine the limit evaluates to one, so the same code
   is sequential there. A timing-based test would be unreliable on a two-core runner. A better step is to
   make the limit injectable, assert the configured degree without timing, and only then raise the floor.

3. **Popup latency baseline is about 1.4 s.** This is a Phase 6 item (target under 500 ms), not a defect in
   the current work, but it should be measured again after ReadyToRun and single-file publishing are
   enabled.

## Unchanged constraints

Offline operation only, no network calls, local files only. RAR remains extract-only and must never appear
in an output format list. The image engine stays Magick.NET-Q8-AnyCPU on the 14.x line; version 15.x drops
the `net8.0` asset and is not usable on .NET 9 yet.
