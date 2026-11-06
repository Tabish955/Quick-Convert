# Phase 2 hand-off message (copy/paste)

Ready to send as-is. Swap the name, and adjust the branch if you're handing over `main`.

---

Hey Ali, Antigravity is done on Windows and Phase 1 is basically closed, so you're up for Phase 2. Pull
`main` first, there's a lot new since you last looked: `ROADMAP.md` (rewritten, read that first),
`PHASE_2_SPEC.md` (the one in git was 9,454 bytes of null characters, seriously), `docs/AUDIT_PHASE_1.md`,
`docs/PHASE_1_CLOSEOUT.md`, `LICENSE`, `THIRD-PARTY-NOTICES.md`, a `tests/QuickConvert.Core.Tests` project,
a Windows CI workflow and a proper `.gitignore`.

Quick summary of what the Windows run changed: it caught a real failing test (`GetCommonTargetFormats`
order test was wrong, our fault, the assertion could never pass because `.jpg` excludes itself as a
target), fixed it correctly, and added `ProgressPercentage = 100` on job completion so a finished job
can't sit at 97%. Good catches. Tests themselves are sound.

**Three things still need eyes before we call Phase 1 closed**, and honestly they're quick:

- **CI has never actually run.** Zero workflow runs on the repo, even after two commits that were literally
  made to trigger it. The YAML is valid, so I'd bet Actions is switched off in Settings → Actions →
  General. Flip it on, push something small, and get one green run.
- **Nobody has confirmed the app actually opens.** Launch the dashboard, then launch again with one file,
  then again with two files while the dashboard is open and make sure only one `QuickConvert.UI.exe` is
  running and the popup comes from it. If it dies on startup, it's the WPF-UI theme dictionary paths in
  `App.xaml` (4.3.0 moved them), there's a note about it in `docs/PHASE_1_VERIFY_PROMPT.md`.
- If you do those two, put the result in `docs/PHASE_1_CLOSEOUT.md` so we stop re-litigating it.

**Then Phase 2. Target: one real conversion path we trust — JPG to PNG through Magick.NET.**

Everyone knows the skeleton is fine. What we don't have is a single real conversion: everything goes
through `MockConversionEngine`, which fakes 2.5 seconds of progress and writes a text file with a `.png`
extension on it. The app looks like it converts and doesn't. That's the whole phase.

Six things, in this order, because 1 and 2 block everything else:

1. `ConversionMatrix`, one table of source → allowed targets for the 25 formats, replacing the hardcoded
   lists in `FormatRegistry`. Table is already written out in `PHASE_2_SPEC.md` §2, copy it. `.rar` can be
   a source, never a target (licence), and nothing goes in that we can't actually convert.
2. `IConversionEngineRouter`. `App.xaml.cs:67` registers exactly one `IConversionEngine` as a singleton, so
   the day the image engine and FFmpeg both exist, one of them is unreachable. Router maps
   `(source, target) → engine`. After that, only the coordinator calls `ConvertAsync`.
3. `QuickConvert.Engines.Images` with Magick.NET-Q8-AnyCPU (Apache 2.0, no licence drama). JPG, PNG, WEBP,
   BMP, TIFF, GIF, ICO. `AutoOrient()` so phone photos aren't sideways, `photo (1).png` naming, real
   progress from the encode, and no half-written file when someone cancels.
4. Capability gating in the UI. `CanConvert` is in the interface and called exactly zero times in the whole
   codebase. Combo boxes show only what the router can really produce. If a user can pick ZIP → PNG, we
   failed.
5. Harden the launch path. `SendFilesToPrimaryAsync` calls `Environment.Exit(0)` when the pipe connect
   fails, which silently throws away the files the user right-clicked. Bounded retry, exit only from `App`,
   global exception handler, rolling log file. We have zero logging today, so every bug report is "it
   doesn't work".
6. One execution path. The popup takes `BatchConversionCoordinator` and never uses it, it just loops files
   one by one, so popup conversions are serial and invisible to the dashboard.

Plus the half-day shell spike (D2 in `ROADMAP.md`): a per-user `HKCU` verb pointed at the built exe, just to
see how the right-click feels on Windows 11 and how long the popup takes with 20 files. Throwaway code,
don't polish it.

Don't start on theming, tray icon or animations yet. Polishing a fake converter is wasted time.

**Done looks like:** right-click 10 JPGs, get 10 real PNGs in parallel, all open in Photos, no duplicate
names, cancel works mid-batch, ZIP → PNG is refused instead of crashing, RAR is never offered as output.

Also, I rewrote `.gitignore` properly: build output, `TestResults`, coverage, downloaded tools in
`tools/`, installer output, `.vs`/`.vscode`, logs, dumps, `.env`/`*.pfx`/`*.snk`, temp files, OS junk. If
you add anything that gets generated rather than written, put it in there instead of committing it. Nothing
junk is currently tracked, I checked, so we're clean.

`dotnet test tests/QuickConvert.Core.Tests` before every push, add a test when you add behaviour, one task
per PR with a `core:` / `img:` / `ui:` / `platform:` prefix.

Ping me if the spec looks wrong anywhere, and if you find a mistake fix it in the same PR. That's exactly
how we ended up with a 9 KB file of nulls last time.
