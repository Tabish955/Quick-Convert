# Phase 1 build checklist — run this on a Windows box

**Why this exists:** the WPF project targets `net9.0-windows`, so it can only be built and launched on
Windows. Everything below assumes a machine with Visual Studio 2022 (.NET desktop workload) and the .NET 9
SDK, and takes about twenty minutes. Work top to bottom and write the result into
`docs/PHASE_1_CLOSEOUT.md`.

---

## What to do

You are working in the Quick-Convert repository: a .NET 9 WPF app that adds an offline file converter to
the Windows File Explorer context menu. Four developers work on it in phases. Phase 1 (solution skeleton,
single-instance IPC, batch coordinator, MVVM shell, licence files, CI, Core unit tests) has been written
but **never compiled** — it was authored in an environment without a .NET SDK. Your job is to make it
build, make the tests pass, and confirm the app actually runs. Do not start Phase 2 features.

### Environment

- Windows 10 1809+ or Windows 11, Visual Studio 2022 with the *.NET desktop development* workload, .NET 9
  SDK (9.0.100 or later).
- Run `git switch arena/14036ad6-quick-convert` (or work on whatever branch the team tells you) and
  `git pull`. Read, in this order: `ROADMAP.md`, `PHASE_2_SPEC.md` §0, `docs/AUDIT_PHASE_1.md`.
- `global.json` pins SDK 9.0.100 with `rollForward: latestFeature`. If the installed SDK is not 9.0.1xx,
  fix `global.json` or install the right SDK — do not delete the file.

### Tasks, in order

**1. Restore and build.**

```
dotnet restore QuickConvert.sln
dotnet build QuickConvert.sln -c Release
```

Fix every error. Things the review flagged as plausible first failures — check these before going deeper:

- `App.xaml` merges `pack://application:,,,/Wpf.Ui;component/Resources/Theme/Light.xaml` and
  `.../Resources/Wpf.Ui.xaml`. That is the WPF-UI 2.x/3.x convention; the project references **WPF-UI
  4.3.0**, which moved to `ui:ThemesDictionary` / `ui:ControlsDictionary`. If startup throws a
  `IOException` about a missing resource, replace those two `<ResourceDictionary Source=.../>` entries
  with the 4.x dictionaries (and add `xmlns:ui` to `App.xaml`).
- `Microsoft.Extensions.DependencyInjection` is pinned to `10.0.14` in a net9.0 solution. If restore
  warns or fails, move both `Microsoft.Extensions.DependencyInjection` references to the latest 9.0.x.
- `MainDashboardViewModel` has both `AddFiles(IEnumerable<string>)` and a `[RelayCommand] AddFiles()`.
  If the MVVM Toolkit generator produces CS0121 (ambiguous call), rename the parameterless one to
  `AddFilesFromDialog` and update `MainDashboardWindow.xaml`'s binding accordingly.
- `Directory.Build.props` turns on `latest-recommended` analyzers. Warnings are fine; do not raise them
  to errors.

**2. Run the tests.**

```
dotnet test tests/QuickConvert.Core.Tests -c Release
```

The suite covers `FormatRegistry` (category mapping, self-target exclusion, deterministic multi-file
ordering, "RAR is never a target"), `ConversionJob.FromFilePath` and `BatchConversionCoordinator`
(enqueue, dedupe, completion, failure, cancel, clear). All of it should pass. If a test fails, decide
whether the test or the code is wrong and say which in the commit message — do not delete a test to get
green. `NoSourceOffersRarAsATarget` is a licence guard: if it fails, remove the RAR target, never the test.

**3. Prove the app runs, three scenarios.**

- `dotnet run --project src/QuickConvert.UI` → the dashboard window opens, no unhandled exception.
- `dotnet run --project src/QuickConvert.UI -- "<path to a real .jpg>"` → the quick popup appears near the
  cursor and lists target formats.
- With the dashboard already open, run the second command again with two files → it must **not** open a
  second app instance; the popup should appear from the first process (this exercises the mutex + named
  pipe + 150 ms debounce). Verify in Task Manager that exactly one `QuickConvert.UI.exe` survives.

Note: conversions still go through `MockConversionEngine`, so the "output" will be a text file with the
target extension. That is expected in Phase 1 — do not try to fix it, that is Phase 2.

**4. Verify CI.** Push the branch, open the Actions tab, confirm the `build` workflow is green on
`windows-latest`. If `Setup .NET` fails on `global-json-file`, check that `global.json` is committed and
in the repo root.

**5. Report back.** Commit with a clear message for each fix. Then write a short summary for the team
containing: commands run, exact errors you hit and how you fixed them, test results (pass/fail counts),
which of the three runtime scenarios worked, CI run URL, and anything in `PHASE_2_SPEC.md` that turned out
to be wrong. Keep it factual, we use it as the Phase 1 close-out record.

### Guardrails

- **Do not start Phase 2 work**: no `ConversionMatrix`, no engine router, no Magick.NET, no new UI. Phase 2
  has its own spec and owner.
- Do not change the format matrix, do not add packages, do not bump WPF-UI majors beyond what is needed to
  make 4.3.0 work.
- Do not rename or delete `ROADMAP.md`, `PHASE_2_SPEC.md`, `docs/AUDIT_PHASE_1.md`, `LICENSE` or
  `THIRD-PARTY-NOTICES.md`.
- If a fix requires a judgement call about scope or licences, stop and ask instead of guessing.
- Commit in small, single-concern commits (`fix:`, `chore:`, `ci:` prefixes).
