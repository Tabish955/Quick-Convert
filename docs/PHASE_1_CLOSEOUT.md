# Phase 1 close-out record

**Date:** 2026-11-06
**Branch checked:** `arena/14036ad6-quick-convert` @ `42e7b84` (also merged to `main` @ `56fa238`)
**Verifier:** repo inspection + GitHub API (no .NET SDK in the verification environment)

---

## Verdict

Phase 1 is **code-complete**. The build/test verification was attempted on Windows and produced two real
fixes — one of which was a genuine bug in my own test — but the verification is **not finished**: the CI
workflow has never executed, the three runtime scenarios are unconfirmed, and no report was filed.

| # | Demand (from `docs/PHASE_1_VERIFY_PROMPT.md`) | Status | Evidence |
|---|---|---|---|
| 1 | `dotnet restore` + `dotnet build -c Release` clean | 🟡 **Probably** | Test suite was run and a failing assertion was found, which implies a successful compile of Core + tests. No build log, no confirmation that the **WPF** project compiled. |
| 2 | `dotnet test` all green | 🟡 **Likely, unconfirmed** | `fd4805a` fixes a real failure (see below). No pass/fail counts reported. |
| 3 | Three runtime scenarios (dashboard, single file, second-instance IPC) | ❌ **Not done** | No commit, no note, no screenshot. `App.xaml` still points at the WPF-UI 2.x/3.x theme dictionary paths while the project references 4.3.0 — if startup throws, this is the cause, and nothing indicates it was exercised. |
| 4 | CI green on `windows-latest` | ❌ **Never ran** | `GET /repos/.../actions/runs` → `total_count: 0`; check-runs on `56fa238` → `0`. Two commits are literally titled "trigger build workflow" and produced no run. |
| 5 | Written report (commands, errors, counts, CI URL) | ❌ **Missing** | No new doc in the diff. |
| — | Guardrails (no Phase 2 work, no new packages, docs intact) | ✅ Respected | diff touches 3 files only. |
| — | Bonus: `ProgressPercentage = 100` on success | ✅ Good fix | `96217df` — correct, the coordinator previously trusted the engine to report 100. |

---

## What the verification actually changed

### `96217df` — `job.ProgressPercentage = 100` on success

```diff
 job.Status = ConversionStatus.Completed;
+job.ProgressPercentage = 100;
 job.OutputFilePath = result.OutputFilePath;
```

Correct and worth keeping: with a real engine (Phase 2) the last progress callback can arrive slightly
before completion, which would leave a finished job showing 97 %. `MockConversionEngine` reported 100
itself, which is why Phase 1 never exposed it.

### `fd4805a` — my test was wrong, and they caught it

`GetCommonTargetFormats_PreservesFirstFilesOrder` asserted `first[0] == single[0]` for a
`png + jpg` selection. But `single[0]` for `.png` is `.jpg`, and `.jpg` **excludes itself** from its own
target list, so the intersection starts at `.jpeg`. The assertion could never pass.

The fix is correct: it uses `png + bmp` for the "order preserved" assertion (where `.jpg` survives), and
keeps a second assertion for the self-exclusion case. Verified by hand against `FormatRegistry` — the
new expectations hold.

> This is the test suite doing its job on its first run. Worth calling out to the team: the tests were
> not decoration.

### `42e7b84` / `56fa238` — `ci.yml` now also triggers on `arena/**`

Harmless, but it was added to `main` as well. Keep it while the session branch lives, then decide: for the
real repo `pull_request: [main]` is enough, and pushes to short-lived branches just burn minutes.

---

## Still open (blocking "Phase 1 closed")

1. **CI has never run.** The workflow file is valid YAML (parsed and checked) and the repo is public, so
   the likely cause is that Actions is disabled for the repository: *Settings → Actions → General →
   Allow all actions and reusable workflows*. Until a green run exists, nobody can claim the solution
   builds.
2. **The WPF app has not been proven to start.** At minimum: launch the dashboard, launch with one file,
   then launch again with two files while the dashboard is open and confirm a single `QuickConvert.UI.exe`
   survives. If `App.xaml` throws on the theme dictionaries, switch to the WPF-UI 4.x
   `ui:ThemesDictionary` / `ui:ControlsDictionary` form.
3. **No report.** Fill in the above two bullets and this table is closed.

---

## Phase 1 deliverable inventory (for the record)

| Item | Where |
|---|---|
| Solution, 4 projects + test project | `QuickConvert.sln` |
| Models / interfaces / coordinator / IPC | `src/QuickConvert.Core/` |
| WPF shell (popup + dashboard) | `src/QuickConvert.UI/` |
| Engine stubs (Phase 3 / 4) | `src/QuickConvert.Engines.*/` |
| Unit tests (24) | `tests/QuickConvert.Core.Tests/` |
| CI | `.github/workflows/ci.yml` |
| Licence + third-party notices | `LICENSE`, `THIRD-PARTY-NOTICES.md` |
| Tool policy (no binaries in Git) | `tools/README.md`, `.gitignore` |
| Plan / audit / hand-off | `ROADMAP.md`, `PHASE_2_SPEC.md`, `docs/` |
