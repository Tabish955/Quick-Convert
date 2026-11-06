# Phase 2 hand-off message (copy/paste)

Ready to send as-is. Swap the name at the top, and adjust the branch name if you're not handing over
`main`.

---

Hey Ali, Phase 1 is properly closed now, so you're up for Phase 2. Pull the repo first, there's a lot
new: `ROADMAP.md` (rewritten, read this one first), `PHASE_2_SPEC.md` (the old one in git was 9,454 bytes
of null characters, seriously, so it's been rewritten), `LICENSE`, `THIRD-PARTY-NOTICES.md`, a
`tests/QuickConvert.Core.Tests` project, and a Windows CI workflow. Build and tests still need one run on
a real Windows machine, Hamza is doing that with Antigravity, so by the time you start the branch should
compile clean. If it doesn't, tell me before you touch anything.

**Where we actually are:** the skeleton is good. Single-instance mutex, named pipe with the 150 ms
debounce, batch coordinator with `SemaphoreSlim` and cancellation, both windows on MVVM + DI. All of that
works and you should build on it, not around it.

What we don't have is a single real conversion. Every file goes through `MockConversionEngine`, which
fakes 2.5 seconds of progress and writes a text file with a `.png` extension on it. So the app looks like
it converts things and doesn't. That's your job this phase.

**Phase 2 target: one real conversion path we can trust, JPG to PNG through Magick.NET.**

Six things, in this order, because 1 and 2 block everything else:

1. `ConversionMatrix`, one table of source → allowed targets for our 25 formats, replacing the hardcoded
   lists in `FormatRegistry`. The full table is already written in `PHASE_2_SPEC.md` §2, copy it. Two
   rules that are not negotiable: `.rar` can be a source but never a target (licence), and nothing goes in
   the matrix that we can't actually convert.
2. `IConversionEngineRouter`. Right now `App.xaml.cs:67` registers exactly one `IConversionEngine` as a
   singleton, so the day the image engine and the FFmpeg engine both exist, one of them is unreachable.
   Router takes `(sourceExt, targetExt)` and gives you the engine. After this, nothing but the coordinator
   is allowed to call `ConvertAsync`.
3. `QuickConvert.Engines.Images` with Magick.NET-Q8-AnyCPU (Apache 2.0, so no licence drama). JPG, PNG,
   WEBP, BMP, TIFF, GIF, ICO. `AutoOrient()` so phone photos aren't sideways, unique names like
   `photo (1).png`, real progress from the actual encode, and no half-written file left behind if someone
   cancels.
4. Capability gating in the UI. `CanConvert` is declared in the interface and never called once in the
   whole codebase. The combo boxes should only show formats the router can really produce. If a user can
   pick ZIP → PNG, we've failed.
5. Hardened launch path. `SendFilesToPrimaryAsync` calls `Environment.Exit(0)` when the pipe connect fails,
   which silently throws away the files the user right-clicked. Add a bounded retry, take the exit out of
   the library, add `PipeOptions.CurrentUserOnly` (already done on the server), plus a global exception
   handler and a rolling log file. Right now we have zero logging, so every bug report is "it doesn't work".
6. One execution path. The popup takes `BatchConversionCoordinator` in its constructor and never uses it,
   it just loops files one at a time, so popup conversions are serial and invisible to the dashboard.
   Route it through the coordinator.

There's also a half-day shell spike in the spec (§3 of `PHASE_2_SPEC.md` / D2 in `ROADMAP.md`). Just a
per-user `HKCU` verb pointed at the built exe so we can see how the right-click actually feels on Windows
11 and measure the time-to-popup with 20 files. Throwaway code, don't polish it, we just need the answer.

Don't start on theming, the tray icon or animations. Polishing a fake converter is wasted time.

**Done looks like:** right-click 10 JPGs in Explorer, get 10 real PNGs in parallel, all of them open in
Photos, no duplicate names, cancel works mid-batch, ZIP → PNG is refused in the UI instead of crashing,
and RAR is never offered as an output.

Run `dotnet test tests/QuickConvert.Core.Tests` before every push, add a test when you add behaviour, and
keep each task in its own PR with a `core:` / `img:` / `ui:` / `platform:` prefix so the rest of us can
review without stepping on each other.

One more thing: we're keeping the repo MIT, but FFmpeg and Pandoc are GPL, so before we drop those binaries
into `tools/` we need the notices updated. `tools/README.md` explains the setup and `THIRD-PARTY-NOTICES.md`
has the rules. Not your problem this phase, just don't be surprised when it comes up.

Ping me if the spec looks wrong anywhere. If you find a mistake, fix the spec in the same PR, that's
exactly how we ended up with a 9 KB file of nulls last time.
