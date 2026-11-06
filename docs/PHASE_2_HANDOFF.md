# Phase 2 hand-off message (copy/paste)

Ready to send as-is. Swap the name at the top.

---

Hey Ali, I have done with phase 1. Phase 2 is yours now, here is the full picture.

Pull main first, then read ROADMAP.md and after that PHASE_2_SPEC.md. Roadmap is the plan, spec is what you
actually have to build. Read section 3 of the roadmap too, it explains why we are touching the Explorer
menu this early.

Where we stand: the skeleton works. Single instance, named pipe with the 150 ms debounce, batch coordinator
with cancellation, popup and dashboard. What we don't have is a single real conversion. Everything goes
through MockConversionEngine, which fakes progress for 2.5 seconds and writes a text file with a .png
extension. So the app looks like it converts and the output opens nowhere.

Goal for phase 2: JPG to PNG actually working through Magick.NET, plus the layer that makes adding more
engines later possible without rewriting anything.

Work items, in this order:

1. ConversionMatrix, half a day. One table, source to allowed targets, only our 25 formats, replacing the
hardcoded lists in FormatRegistry. The table is already written in spec section 3, copy it from there. rar
is input only and never an output format, that's a licence condition and there's a test that checks it.

2. IConversionEngineRouter, half a day. App.xaml.cs line 67 registers exactly one IConversionEngine today,
so the day we have two engines one of them is unreachable. The router takes source and target and returns
the engine, and it becomes the only place the interface asks for the list of formats it can offer.

3. Engines.Images project with Magick.NET-Q8-AnyCPU, two days. Read jpg png webp bmp tiff gif ico, write the
same plus pdf. AutoOrient so phone photos aren't sideways, real progress from the encode, unique names like
photo (1).png, delete the partial file on cancel, and never throw out of ConvertAsync, return a failed
result with a reason instead.

4. Capability gating. CanConvert is never called anywhere in the codebase right now. Every dropdown should
only show formats the router can really produce, and anything that can't convert should be refused before
it reaches the queue.

5. Launch path, about a day. SendFilesToPrimaryAsync calls Environment.Exit(0) when the pipe connection
fails, which silently throws away the files the user selected. Add a retry, keep the exit inside App only,
add a global exception handler and a log file under %LOCALAPPDATA%\QuickConvert\logs. We have no logging at
all at the moment.

6. One execution path, about a day. The popup runs its own loop over the files, so popup conversions are
serial and don't appear on the dashboard. Route it through BatchConversionCoordinator, like the dashboard
already does.

7. Half day spike: add a per-user HKCU registry verb pointing at the built exe, with MultiSelectModel set.
Check where the entry shows up on Windows 11, time the popup with 1 file and with 20 files, and count how
many processes start. Throwaway code, don't polish it, we only need the measurements.

Don't touch theming or the tray icon until items 4 and 6 are merged.

Done means: select 10 jpgs, get 10 real pngs in parallel, all opening in Photos, no duplicate names, cancel
mid batch leaves nothing behind, zip to png is refused in the UI instead of crashing, and rar is never
offered as an output.

Two things still open from phase 1, close them first because they're quick: CI has never actually run
(turn on Actions in the repo settings, push something small, get a green tick), and nobody has checked the
second instance behaviour by hand (open the dashboard, then run the app again with one file and then with
two files, you should get one popup and exactly one QuickConvert.UI.exe in Task Manager). Put both results
in docs/PHASE_1_CLOSEOUT.md.

Rules: dotnet test before every push, a test whenever you add behaviour, one task per PR with a core: /
img: / ui: / platform: prefix, and anything generated goes in .gitignore instead of into a commit.

Ping me if the spec looks wrong anywhere.
