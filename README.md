# Quick-Convert

An offline, $0-cost, privacy-first file converter that lives in the Windows File Explorer right-click menu. No cloud, no accounts, no upload limits. Built on .NET 9 (`net9.0` / `net9.0-windows`).

> **Current state (Phases 1–5 complete):** Image conversion (Magick.NET), audio/video conversion and extraction (FFmpeg), document conversion (Pandoc + wkhtmltopdf), spreadsheets (MiniExcel), archive extraction and compression (SharpCompress + 7za), Explorer shell integration, and Inno Setup packaging are complete. 283 tests pass locally with 100% offline security.

---

## Scope

**~25 essential formats.** Deliberately narrow — quality over quantity.

| Category | Formats |
|---|---|
| Image | JPG, PNG, WEBP, BMP, TIFF, GIF (+ PDF output) |
| Audio | MP3, WAV, FLAC, AAC, OGG, M4A |
| Video | MP4, MKV, WEBM, MOV (+ audio extraction) |
| Document | TXT, MD, HTML, DOCX, PDF, CSV, JSON |
| Archive | ZIP, 7Z, TAR, GZ — **RAR is extract-only** (never created; unRAR is licence-restricted) |
| E-book | EPUB, MOBI (later phase) |

## How it works

1. Select one or many files in File Explorer and pick **Quick Convert** from the context menu.
2. A compact popup opens next to the cursor with only the target formats that are valid for the whole selection.
3. Convert. Files are written next to the originals (or to a folder you choose) with non-destructive naming (`photo (1).png`).

Multi-file right-click starts one process per file; a global mutex plus a named pipe (`QuickConvert_Explorer_Pipe`) with a 150 ms debounce collapses those launches into a single popup.

---

## Solution layout

```
Quick-Convert/
├── src/
│   ├── QuickConvert.Core/            # net9.0 — models, interfaces, format matrix, batch coordinator, IPC
│   │   ├── Models/                   # ConversionJob, ConversionOptions, ConversionResult, ConversionStatus, FileCategory
│   │   ├── Interfaces/               # IConversionEngine
│   │   ├── Services/                 # FormatRegistry, MockConversionEngine, BatchConversionCoordinator
│   │   └── Ipc/                      # SingleInstancePipeService (Mutex + Named Pipe + debounce)
│   ├── QuickConvert.Engines.Media/   # net9.0 — FFmpeg engine (Phase 3, currently a stub that throws)
│   ├── QuickConvert.Engines.Docs/    # net9.0 — documents + archives (Phase 4, currently a stub that throws)
│   └── QuickConvert.UI/              # net9.0-windows WPF app (WPF-UI FluentWindow)
│       ├── ViewModels/               # QuickPopupViewModel, MainDashboardViewModel
│       ├── Views/                    # QuickPopupWindow, MainDashboardWindow
│       ├── Converters/               # InverseBoolConverter
│       └── App.xaml(.cs)             # DI, IPC routing, startup
├── docs/AUDIT_PHASE_1.md             # Phase 1 audit + Phase 2 recommendations
├── PHASE_2_SPEC.md                   # Active phase spec (single source of truth for Phase 2)
├── tools/                            # Local CLI binaries, NOT in Git: ffmpeg/, 7z/, pandoc/
├── global.json                       # Pinned to .NET SDK 9.0.100 (rollForward: latestFeature)
└── QuickConvert.sln
```

## Installation & Packaging

### Standalone Installer (Inno Setup 6)
Download the latest `QuickConvert-Setup-1.0.0.exe` from GitHub Releases.
- **Per-User Installation:** Installs to `%LOCALAPPDATA%\Programs\QuickConvert` without requiring administrator privileges or UAC elevation.
- **Bundled Tools:** Automatically bundles all required CLI conversion tools (`ffmpeg`, `pandoc`, `wkhtmltopdf`, `7za`) in a solid LZMA2 compressed installer (~99 MB).
- **SmartScreen:** As an unsigned open-source utility, Windows SmartScreen will display an informational warning on initial launch. Click **`More info`** -> **`Run anyway`**. See [docs/SMARTSCREEN.md](docs/SMARTSCREEN.md) for details.

### Context Menu Integration
The installer registers the `"Convert with QuickConvert"` context menu verb across 34 supported source file types in `HKCU\Software\Classes\SystemFileAssociations\<ext>\shell\QuickConvert`.

To manually register or unregister shell verbs from the CLI:
```powershell
QuickConvert.UI.exe --register-shell     # Registers context menu verbs in HKCU
QuickConvert.UI.exe --unregister-shell   # Cleanly removes all registered verbs
QuickConvert.UI.exe --version            # Prints version information
```

### Uninstallation
Uninstall via **Windows Settings -> Apps -> Installed apps** (or Control Panel). The uninstaller cleanly removes all registered shell verbs and manifest files without leaving residual registry entries.

## Building

**Prerequisites:** .NET 9 SDK (9.0.100+), Windows 10 1809+ or Windows 11. Visual Studio 2022 with the *.NET desktop development* workload for the WPF project.

```bash
dotnet build QuickConvert.sln
dotnet test tests/QuickConvert.Core.Tests      # Core unit tests
dotnet run --project src/QuickConvert.UI
```

The WPF project targets `net9.0-windows`, so the full solution only builds on Windows. CI runs on `windows-latest` (`.github/workflows/ci.yml`).

Try the IPC path without Explorer integration:

```bash
dotnet run --project src/QuickConvert.UI -- "C:\path\to\photo.jpg" "C:\path\to\other.jpg"
dotnet run --project src/QuickConvert.UI            # second launch without files opens the dashboard
```

## Roadmap

Full plan, exit criteria and decision gates: **[ROADMAP.md](ROADMAP.md)**.

- [x] **Phase 1 — Skeleton, IPC, contract.** Solution, models, `IConversionEngine`, single-instance mutex + named-pipe IPC with debounce, `BatchConversionCoordinator`, MVVM shell, licence + notices, CI, Core unit tests.
- [x] **Phase 2 — First real conversion.** `ConversionMatrix`, `IConversionEngineRouter`, Magick.NET image engine (JPG/PNG/WEBP/…), capability gating, hardened launch path, Explorer verb spike.
- [x] **Phase 3 — Media engine.** Local FFmpeg for audio/video, hardware acceleration (NVENC/QSV/AMF), audio extraction.
- [x] **Phase 4 — Documents & archives.** Pandoc, PDF output, MiniExcel, SharpCompress + `7za.exe`.
- [x] **Phase 5 — Explorer integration & packaging.** Production shell verb (`HKCU`), Inno Setup 6 installer, startup tool probe, graceful degradation, SmartScreen documentation.
- [ ] **Phase 6 — v1.0 hardening.** Theming, tray, crash reporting (local only), performance, offline audit.

## Licence

Quick-Convert's source is **MIT** (see [LICENSE](LICENSE)). Bundled command-line tools (FFmpeg, 7-Zip, Pandoc) are separate programs under their own licences — see [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md). Binaries are never committed to `tools/`; see [tools/README.md](tools/README.md).

## Non-negotiables

- 100% offline at runtime. No telemetry, no auto-update pings, no network calls.
- No paid services, no licence-encumbered dependencies without a documented decision.
- A format may only be offered in the UI if an engine reports it can actually convert it.

## Contributing

Phase Relay rules (4 developers): one concern per PR, contract changes to `IConversionEngine`/`ConversionJob`/`ConversionOptions` need three approvals, and every new dependency needs a licence note. Full checklist in [PHASE_2_SPEC.md §10](PHASE_2_SPEC.md).
