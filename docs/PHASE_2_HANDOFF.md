# Phase 2 hand-off message (copy/paste)

Ready to send as-is (WhatsApp / Teams / group chat). Update the name at the top before sending.

---

Hey Ali, Phase 1 is done and merged to `main` (`c9e1a32`). Before you start on Phase 2, one thing worth knowing: we have the whole skeleton working, but **nothing actually converts yet**. Right now every conversion goes through `MockConversionEngine`, which fakes 2.5 seconds of progress and then writes a text file with a `.png` extension on it. So if anyone demos the app and says "look, it converted", it didn't.

Also heads up, `PHASE_2_SPEC.md` in the repo was 9,454 bytes of null characters. Completely unreadable, and since we only have one commit in the history it wasn't recoverable. I've rewritten it, so pull `main` before you start or you'll be reading the old broken one.

**What Phase 1 actually gives you (all of this works, just build on it):**
- Solution + projects: `Core`, `Engines.Media`, `Engines.Docs`, `UI`
- Single-instance mutex + named pipe with the 150 ms debounce, so 20 right-clicked files become one batch
- `BatchConversionCoordinator` with `SemaphoreSlim` concurrency, per-job cancellation, progress events
- Both windows (quick popup + dashboard) wired with MVVM + DI
- WPF-UI FluentWindows, tray package referenced (not used yet)

**What Phase 2 has to deliver: one real, trustworthy conversion path.** JPG to PNG through Magick.NET, from the right-click popup, through the normal coordinator.

Do these in order, they're ordered because 2 and 3 block everyone else:

1. **`ConversionMatrix` (1 day, do this first).** Single table of source → allowed targets, replacing the hardcoded format lists in `FormatRegistry`. Keep only our 25 formats. `.rar` can be a source but must never show up as an output, that's a licence thing not a preference. `PHASE_2_SPEC.md` §2 has the full table written out, just copy it.

2. **`IConversionEngineRouter` (half a day).** Right now we register exactly one `IConversionEngine` as a singleton in `App.xaml.cs:67`, so the moment the image engine and the FFmpeg engine both exist, one of them is dead code. Router takes `(sourceExt, targetExt)` and returns the engine that can do it. Nothing outside the coordinator should be calling `ConvertAsync` directly.

3. **`QuickConvert.Engines.Images` with Magick.NET (2 days).** `Magick.NET-Q8-AnyCPU`, Apache 2.0. JPG/PNG/WEBP/BMP/TIFF/GIF/ICO in and out, PDF out later. Real progress from the actual decode/encode steps, `AutoOrient()` so phone photos aren't sideways, unique names (`photo (1).png`), and make sure a cancelled conversion doesn't leave a half-written file in the output folder.

4. **Make `CanConvert` mean something (half a day).** It's declared in `IConversionEngine` and literally never called anywhere in the codebase. The combo boxes should only ever show formats the router can actually produce. If a user can select ZIP → PNG, we failed.

5. **One execution path.** The quick popup takes `BatchConversionCoordinator` in its constructor and then never uses it, it just loops files one by one. So popup conversions are serial and don't show up on the dashboard. Delete that loop and go through the coordinator.

Don't start on theming, tray icon, or animations yet. UI polish comes after step 3 lands, otherwise you're polishing a fake.

**Done looks like:** right-click 10 JPGs, get 10 real PNGs in parallel, all open in Photos, no duplicates, cancel works mid-batch, and nothing in the UI offers a format we can't make.

One last thing: we said MIT/GPL from the first commit but there's no LICENSE file in the repo yet, and no third-party notices. Before we drop FFmpeg and Pandoc binaries into `/tools`, let's get that right, because those are GPL and it changes what we can ship.

Shout if anything in the spec looks wrong, and put each task in its own PR with the prefix `core:`, `img:`, `ui:` so we can review without stepping on each other.
