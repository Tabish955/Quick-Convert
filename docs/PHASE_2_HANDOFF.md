# Phase 2 hand-off message (copy/paste)

Ready to send as-is. Swap the name at the top.

---

Hey Ali, Phase 1 is done and Phase 2 is yours. I know you haven't had a proper look at the app yet, so
here's the short version before the instructions.

**What exists right now:** a .NET 9 WPF app with four projects (Core, Engines.Media, Engines.Docs, UI), a
single-instance mutex plus a named pipe with a 150 ms debounce (so right-clicking 20 files funnels into one
batch instead of 20 windows), a batch coordinator with concurrency and per-file cancellation, and two
windows on MVVM + DI: the small cursor popup and the dashboard. Plus a test project, a CI workflow, the
licence files, and the docs.

**What doesn't exist:** a single real conversion. Everything runs through `MockConversionEngine`, which
fakes 2.5 seconds of progress and then writes a text file with a `.png` extension on it. So if you launch
the app and convert something, it will look like it worked and the output won't open anywhere. That is the
whole point of Phase 2.

**Get it running first** (Windows, VS2022 with the .NET desktop workload, .NET 9 SDK):

```
git pull
dotnet build QuickConvert.sln
dotnet test tests/QuickConvert.Core.Tests
dotnet run --project src/QuickConvert.UI
```

Should build and the tests should pass. If the app dies on startup instead, it's almost certainly the
WPF-UI theme dictionaries in `App.xaml` (we're on 4.3.0 and those paths moved) — there's a note in
`docs/PHASE_1_BUILD_CHECKLIST.md`.

**Two things I couldn't confirm and want you to close out before the real work:**

1. CI has never actually run. Zero workflow runs on the repo. The YAML is valid, so my guess is Actions is
   switched off under Settings → Actions → General. Turn it on, push something trivial, check the green tick.
2. Nobody has watched the second-instance behaviour with their own eyes. Open the dashboard, then from a
   terminal run the app again with one file, then again with two files. You should get one popup from the
   first process and exactly one `QuickConvert.UI.exe` in Task Manager.

Put both results in `docs/PHASE_1_CLOSEOUT.md` and we can stop talking about Phase 1.

**Phase 2: one real conversion path we trust, JPG to PNG through Magick.NET.**

Six things, in this order, because 1 and 2 block everything else:

1. `ConversionMatrix` — one table of source → allowed targets for our 25 formats, replacing the hardcoded
   lists in `FormatRegistry`. The table is already written in `PHASE_2_SPEC.md` §2, copy it. Two rules:
   `.rar` can be a source but never a target (licence), and nothing goes in that we can't actually convert.
2. `IConversionEngineRouter`. `App.xaml.cs:67` registers exactly one `IConversionEngine` as a singleton, so
   the day the image engine and FFmpeg both exist, one of them is unreachable. The router maps
   `(source, target) → engine`. After that, only the coordinator calls `ConvertAsync`.
3. `QuickConvert.Engines.Images` with Magick.NET-Q8-AnyCPU (Apache 2.0). JPG, PNG, WEBP, BMP, TIFF, GIF,
   ICO. `AutoOrient()` so phone photos aren't sideways, `photo (1).png` naming, real progress from the
   encode, and no half-written file left behind when someone hits cancel.
4. Capability gating in the UI. `CanConvert` exists on the interface and is called zero times in the whole
   codebase. The format dropdowns should only show what the router can really produce. If a user can pick
   ZIP → PNG, we've failed.
5. Harden the launch path. `SendFilesToPrimaryAsync` calls `Environment.Exit(0)` when the pipe connect
   fails, which silently throws away the files the user right-clicked. Add a bounded retry, keep the exit
   in `App` only, add a global exception handler and a rolling log file. We have no logging at all right
   now, which is why every bug report is "it doesn't work".
6. One execution path. The popup takes `BatchConversionCoordinator` in its constructor and never uses it,
   it just loops files one by one. So popup conversions are serial and don't show up on the dashboard.
   Route it through the coordinator.

There's also a half-day spike (D2 in `ROADMAP.md`): a per-user `HKCU` right-click verb pointed at the built
exe, just to feel how it lands on Windows 11 and time the popup with 20 files selected. Throwaway code,
don't polish it.

Don't start on theming, the tray icon or animations. Polishing a fake converter is wasted time.

**Done looks like:** right-click 10 JPGs, get 10 real PNGs in parallel, all open in Photos, no duplicate
names, cancel works mid-batch, ZIP → PNG is refused in the UI instead of blowing up, and RAR is never
offered as an output.

House rules: `dotnet test` before every push, a test whenever you add behaviour, one task per PR with a
`core:` / `img:` / `ui:` / `platform:` prefix, and if something generates a file it goes in `.gitignore`
rather than into a commit. Read `ROADMAP.md` before `PHASE_2_SPEC.md`, it explains why the phases are
ordered like this.

Ping me if the spec looks wrong. If it is wrong, fix it in the same PR — that's how we ended up committing
a 9 KB file of null characters as our hand-off doc.
