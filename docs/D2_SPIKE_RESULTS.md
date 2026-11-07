# D2 spike results (Phase 2) — Windows 11 Verification

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
- `QuickConvert.UI.exe` processes: **1** (Explorer's `MultiSelectModel = "Player"` caps a single process command line at 16 items. For a 20-file selection, Explorer launches 2 processes: 16 items in the first and 4 items in the second. The secondary process handed off its files via Named Pipe within ~190 ms and exited; the primary process's 150 ms debounce window absorbed both arrivals into one unified batch)
- All 20 files present in the popup batch: **YES**

## 4. Implications for D2 (decision gate)
- **Classic HKCU verb acceptable for v1 (D2 option a)?**
  YES. For v1 developer/power-user usage, the HKCU verb requires zero administrative elevation, leaves no residue after registry unregistration, and cleanly delivers batch payloads via the 150 ms pipe debounce mechanism.
- **Is the Win11 "Show more options" placement a discoverability blocker worth the MSIX route (option b) in v1.1?**
  YES. On modern Windows 11 systems, hiding the verb behind "Show more options" reduces discoverability and increases friction (two clicks instead of one). Packaging as a sparse signed MSIX with `IExplorerCommand` is recommended for v1.1 / Phase 5.
