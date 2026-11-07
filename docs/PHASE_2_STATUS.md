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
| Two jobs in one batch could silently overwrite each other's output (`a.jpg` and `a.png` to `a.webp`) | `MagickImageConversionEngine.cs` | `OutputPromotion` moves with `overwrite: false` and takes the next free `name (n)`; 5 tests |
| Stale spec reference, incorrect PDF/Ghostscript comment, wrong shell file-count limits | `ConversionMatrix.cs`, `MagickImageConversionEngine.cs`, `D2_SPIKE_RESULTS.md`, `tools/spike/Add-QuickConvertVerb.ps1` | Corrected |

## Test position

The merged tree carries 60 Core test cases (43 `[Fact]`, 3 `[Theory]` with 17 `[InlineData]`) and 14 Images
facts. These figures match the counts reported for each merge. The RAR guard
(`NoSourceOffersRarAsATarget`) is intact and no format table lists RAR as a target.

## Open items

1. **CI has never run.** GitHub returns `HTTP 422: Actions has been disabled for this user`, which is an
   account-level restriction rather than a repository setting. See `docs/PHASE_1_CLOSEOUT.md` for the
   steps. Until one run is green, the test counts above rest on local runs only.

2. **Parallel conversion limit: resolved.** `BatchConversionCoordinator` takes an optional
   `maxDegreeOfParallelism` (default unchanged) and exposes `MaxDegreeOfParallelism`. Five tests in
   `BatchConversionCoordinatorConcurrencyTests` assert the default, the configured value, rejection of values
   below 1, and, through in-flight counters rather than timing, that a limit of 1 never overlaps and a limit of
   2 runs exactly two at once. A mutant that ignores the limit makes the two behavioural tests fail.
   The floor of one on small machines is unchanged.

3. **Popup latency baseline is about 1.4 s.** This is a Phase 6 item (target under 500 ms), not a defect in
   the current work, but it should be measured again after ReadyToRun and single-file publishing are
   enabled.

## Unchanged constraints

Offline operation only, no network calls, local files only. RAR remains extract-only and must never appear
in an output format list. The image engine stays Magick.NET-Q8-AnyCPU on the 14.x line; version 15.x drops
the `net8.0` asset and is not usable on .NET 9 yet.
