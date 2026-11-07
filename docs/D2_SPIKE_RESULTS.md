# D2 spike results (Phase 2): Windows 11 verification

Script: `tools/spike/Add-QuickConvertVerb.ps1` (throwaway).

## 1. Verb placement on Windows 11
- [ ] Top-level context menu: NO
- [x] Behind "Show more options": YES
- **Notes**: Windows 11 strictly gates the modern top-level context menu behind packaged sparse MSIX identities implementing `IExplorerCommand`. Classic HKCU registry verbs (`SystemFileAssociations\.jpg\shell\QuickConvert`) are routed exclusively to the legacy context menu accessible via "Show more options" (or Shift+F10).

## 2. One file
- Time from click to visible popup: **1407 ms** (run 1 cold: 1494 ms / run 2 warm: 1407 ms)
- `QuickConvert.UI.exe` processes in Task Manager during popup: **1**

## 3. Twenty files (same folder, 20 JPGs)
- Time from click to visible popup: **1490 ms** (run 1) / **2237 ms** (run 2)
- `QuickConvert.UI.exe` processes: **1** (the verb command passes `%1`, so Explorer starts one launcher process per selected file. Each launcher hands its path to the running window over the named pipe and exits. All 20 launches land inside the 150 ms debounce window, so the popup receives a single batch. This matches the risk already recorded in `ROADMAP.md` section 4: "a 20-file selection starts 20 processes". `MultiSelectModel = "Player"` is what keeps the verb visible above the 15-item `Document` limit; Player's own limit is 100)
- All 20 files present in the popup batch: **YES**

## 4. Implications for D2 (decision gate)
- **Classic HKCU verb acceptable for v1 (D2 option a)?**
  YES. For v1 developer/power-user usage, the HKCU verb requires zero administrative elevation, leaves no residue after registry unregistration, and cleanly delivers batch payloads via the 150 ms pipe debounce mechanism.
- **Latency baseline:** about 1.4 s from click to a visible popup (1407 ms warm, 1494 ms cold). The Phase 6
  target is under 500 ms, and the risk register already names ReadyToRun and single-file publishing as the
  remedy, so this is a Phase 6 performance item rather than a reason to change the D2 decision. It does mean
  the debounce window must not be shortened without re-measuring: the launches arrive one per file.
- **Is the Win11 "Show more options" placement a discoverability blocker worth the MSIX route (option b) in v1.1?**
  YES. On modern Windows 11 systems, hiding the verb behind "Show more options" reduces discoverability and increases friction (two clicks instead of one). Packaging as a sparse signed MSIX with `IExplorerCommand` is recommended for v1.1 / Phase 5.
