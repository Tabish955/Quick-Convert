# Quick-Convert

An offline, $0-cost, privacy-first Convertio alternative integrated directly into Windows File Explorer, built on .NET 9 (`net9.0` and `net9.0-windows`).

---

## ⚡ Overview

Quick-Convert allows users to select one or multiple files in Windows File Explorer, right-click, and instantly convert them between hundreds of media, document, spreadsheet, and archive formats without uploading private files to the cloud or paying subscription fees.

### Core Highlights
- **100% Offline & Free:** No cloud API keys, bandwidth limits, or privacy concerns.
- **Convertio Matrix:** Over 50+ file extensions supported across Images, Audio, Video, Documents, Spreadsheets, E-Books, Fonts, and Archives.
- **Windows File Explorer Integration:** Seamless multi-file selection support using a Global Mutex and Named Pipe IPC (`QuickConvert_Explorer_Pipe`) with a 150ms debounce window.
- **Fluent UI Design:** Built with `WPF-UI` (Lepo.co) adhering to modern Windows 11 Fluent design principles (Mica, rounded geometry, Dark/Light modes).
- **Concurrency & Concurrency Control:** Parallel batch conversions orchestrated via `SemaphoreSlim`.

---

## 🏗 Solution Architecture

The solution (`QuickConvert.sln`) is divided into 4 isolated projects:

```
Quick-Convert/
├── src/
│   ├── QuickConvert.Core/             # .NET 9.0 Class Library
│   │   ├── Models/                    # Enums, ConversionJob, ConversionOptions, ConversionResult
│   │   ├── Interfaces/                # IConversionEngine contract
│   │   ├── Services/                  # FormatRegistry, MockConversionEngine, BatchConversionCoordinator
│   │   └── Ipc/                       # SingleInstancePipeService (Mutex + Named Pipe + Debounce)
│   ├── QuickConvert.Engines.Media/    # .NET 9.0 Class Library (Phase 3 FFmpeg Stub)
│   ├── QuickConvert.Engines.Docs/     # .NET 9.0 Class Library (Phase 4 Pandoc & 7-Zip Stub)
│   └── QuickConvert.UI/               # .NET 9.0 WPF Application (net9.0-windows)
│       ├── ViewModels/                # QuickPopupViewModel, MainDashboardViewModel
│       ├── Views/                     # QuickPopupWindow, MainDashboardWindow
│       ├── Converters/                # InverseBoolConverter, BoolToVis
│       ├── App.xaml / App.xaml.cs     # DI, IPC Routing, Startup handling
│       └── QuickConvert.UI.csproj
├── tools/                             # Local CLI tools directory
│   ├── ffmpeg/.gitkeep
│   ├── pandoc/.gitkeep
│   └── 7z/.gitkeep
├── PHASE_2_SPEC.md                    # Developer handoff guide for UI Designer
├── WHATSAPP_PHASE_2_MSG.txt           # WhatsApp handoff communication
├── global.json                        # Pinned to .NET 9.0 (latestFeature)
├── .gitignore                         # Visual Studio .gitignore
└── QuickConvert.sln                   # Visual Studio Solution
```

---

## 🚀 Getting Started

### Prerequisites
- [.NET 9.0 SDK](https://dotnet.microsoft.com/download/dotnet/9.0) (SDK 9.0.100+)
- Windows 10 (1809+) or Windows 11

### Build Solution
```bash
dotnet build QuickConvert.sln
```

### Run UI App
```bash
dotnet run --project src/QuickConvert.UI
```

---

## 📋 Project Roadmap

- [x] **Phase 1 (Completed):** Solution scaffolding, FormatRegistry, SingleInstance Mutex + Named Pipe IPC, MockConversionEngine with realistic 2.5s progress increments, Batch queue coordinator, MVVM shell.
- [ ] **Phase 2:** Fluent UI redesign with `WPF-UI` controls, smooth animations, and system tray integration.
- [ ] **Phase 3:** High-performance Media Engine integrating local FFmpeg binaries for audio/video transcoding and hardware acceleration (NVENC, QSV, AMF).
- [ ] **Phase 4:** Document & Archive Engine integrating Pandoc and 7-Zip CLI orchestrators.
- [ ] **Phase 5:** Windows Explorer Shell Extension (`IExplorerCommand`) and MSIX/InnoSetup packaging.
