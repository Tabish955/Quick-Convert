# Third-party notices

Quick-Convert's own source code is MIT licensed (see `LICENSE`). The libraries and tools below are used
under their own licences. This file must be updated **in the same PR** that adds a dependency — a
`tools/` binary with no entry here is a release blocker.

Status legend: **[in repo]** referenced by a `.csproj` today · **[planned]** decided, not yet referenced.

---

## [in repo] CommunityToolkit.Mvvm — MIT
`ObservableObject`, `[ObservableProperty]`, `[RelayCommand]`. Source: https://github.com/CommunityToolkit/dotnet

## [in repo] Microsoft.Extensions.DependencyInjection — MIT
DI container. Source: https://github.com/dotnet/runtime

## [in repo] WPF-UI (Wpf.Ui) — MIT
Fluent window chrome and controls. Source: https://github.com/lepoco/wpfui
> Pin the version in `QuickConvert.UI.csproj` and note here when it changes; the theme dictionary paths
> moved between 2.x/3.x/4.x and a silent major upgrade breaks startup.

## [in repo] Hardcodet.NotifyIcon.Wpf — CPOL 1.02
Tray icon. **Currently referenced but unused** — either wire it up in Phase 6 or remove the package.
Source: https://github.com/hardcodet/wpf-notifyicon

## [in repo] Magick.NET — Apache-2.0
Image engine (Phase 2). Wraps ImageMagick, which is under the ImageMagick License (Apache-2.0
compatible). Package: `Magick.NET-Q8-AnyCPU` version 14.17.2. Source: https://github.com/dlemstra/Magick.NET

## [in repo] MiniExcel — Apache-2.0
CSV/JSON/XLSX reader and writer in `QuickConvert.Engines.Docs`. Package: `MiniExcel` version 1.41.0. Source: https://github.com/mini-software/MiniExcel
> **ClosedXML is deliberately not used.** It pulls `DocumentFormat.OpenXml` and `SixLabors.Fonts`
> (Six Labors Split Licence: free only under revenue/OSS conditions) plus significant weight.

## [in repo] SharpCompress — MIT
zip / tar / gz / RAR **extraction**, pure managed in `QuickConvert.Engines.Docs`. Package: `SharpCompress` version 0.40.0. Source: https://github.com/adamhathcock/sharpcompress
> Using SharpCompress for RAR keeps the 7-Zip unRAR restriction out of our bundle entirely.

## [in repo] xUnit + Microsoft.NET.Test.Sdk — Apache-2.0 / MIT
Unit and integration test runner. Referenced by test projects only (`QuickConvert.Core.Tests`, `QuickConvert.Engines.Images.Tests`, `QuickConvert.Engines.Media.Tests`, `QuickConvert.Engines.Docs.Tests`), not shipped in binary distribution.

## [planned] PdfSharp — MIT
PDF output. Source: https://github.com/empira/PDFsharp

---

## Bundled executables (Phase 3+, downloaded by script, not committed)

These are **separate programs invoked as subprocesses** (mere aggregation). Our MIT code is not linked
against them, so their licences do not extend to our source — but we do have to honour them.

### FFmpeg — GPL v3.0 (Decision D1: Resolved)
- **Version:** 9.0.2 essentials release by Gyan.dev
- **Archive URL:** `https://www.gyan.dev/ffmpeg/builds/packages/ffmpeg-9.0.2-essentials_build.zip`
- **Fallback URL:** `https://github.com/GyanD/codexffmpeg/releases/download/9.0.2/ffmpeg-9.0.2-essentials_build.zip`
- **SHA-256 Checksum:** `60f467265b1e312373dbcd92200c2618a74850f98d3d078e94296bb3fa2047ba`
- **Licence:** GNU General Public License v3.0 (GPLv3) due to compilation with `--enable-gpl` and `--enable-version3`.
- **Selected Build Configuration:** Includes `libx264` (H.264/AVC), `libx265` (HEVC), `libvpx` (VP8/VP9), `libopus`, `libmp3lame`, `libvorbis`, and hardware encoder wrappers (`nvenc`, `qsv`, `amf`).
- **Source Offer & Access:** Upstream source corresponding to this binary is available at https://ffmpeg.org/download.html and on GitHub at https://github.com/FFmpeg/FFmpeg (tag `n9.0.2`). Packaging build scripts are archived at https://github.com/GyanD/codexffmpeg.
- **Architectural Isolation:** Quick-Convert executes `ffmpeg.exe` and `ffprobe.exe` exclusively as standalone external processes over process pipes (`ProcessStartInfo.ArgumentList`), maintaining clean architectural and licensing isolation under mere aggregation principles.

### Pandoc — GPL-2.0-or-later
- **Version:** 3.5 (separate executable under `tools/pandoc/`, downloaded by `tools/Get-Pandoc.ps1`).
- **Archive URL:** `https://github.com/jgm/pandoc/releases/download/3.5/pandoc-3.5-windows-x86_64.zip`
- **SHA-256 Checksum:** `887E980E2228D8D45A431936E8BC6270A50CC9A5A4EC7E62064A3A5DE6423B71`
- **Licence:** GNU General Public License v2.0 or later (GPL-2.0-or-later).
- **Source Access:** Upstream repository and source code at https://github.com/jgm/pandoc.
- **Architectural Isolation:** Invoked as a pure subprocess via `ToolProcessRunner` using `ProcessStartInfo.ArgumentList`. Offline operation guaranteed.

### wkhtmltopdf — LGPL-3.0
- **Version:** 0.12.6-1 (separate executable under `tools/wkhtmltopdf/`, downloaded by `tools/Get-Wkhtmltopdf.ps1`).
- **Archive URL:** `https://github.com/wkhtmltopdf/packaging/releases/download/0.12.6-1/wkhtmltox-0.12.6-1.msvc2015-win64.exe`
- **SHA-256 Checksum:** `1EFCFAF8472871268FD399865A3E62B435B91D4413009B80830EAAFF7A57D088`
- **Licence:** GNU Lesser General Public License v3.0 (LGPL-3.0).
- **Source Access:** Upstream repository and source code at https://github.com/wkhtmltopdf/wkhtmltopdf.
- **Architectural Isolation:** Invoked as a pure subprocess via `ToolProcessRunner` using `ProcessStartInfo.ArgumentList` with `--disable-javascript`, `--load-error-handling ignore`, and `--enable-local-file-access` for secure offline PDF rendering.

### 7-Zip (`7za.exe`) — LGPL-2.1 (with unRAR restriction on the RAR code)
- **Version:** 23.01 (separate executable under `tools/7z/`, downloaded by `tools/Get-7za.ps1`).
- **Archive URLs:** `https://www.7-zip.org/a/7zr.exe` and `https://www.7-zip.org/a/7z2301-extra.7z`.
- **SHA-256 Checksums:**
  - `7zr.exe`: `72C98287B2E8F85EA7BB87834B6CE1CE7CE7F41A8C97A81B307D4D4BF900922B`
  - `7z2301-extra.7z`: `DB3A1CBE57A26FAC81B65C6A2D23FEAECDEEDE3E4C1FE8FB93A7B91D72D1094C`
- **Licence:** GNU Lesser General Public License v2.1.
- **Architectural Isolation:** Used exclusively to create `.7z` archives via `7za.exe` subprocess. RAR extraction is handled by SharpCompress, and RAR generation is strictly forbidden by the product matrix.

---

## Compliance checklist per release

- [x] Every `PackageReference` and every file in `tools/` has an entry above.
- [x] No GPL *library* is referenced from our assemblies (a GPL **executable** we shell out to is fine).
- [ ] Licence texts for bundled tools are included in the installer output.
- [x] The source offer link for GPL tools resolves publicly.
- [x] No telemetry, analytics or crash-upload SDK anywhere in the dependency graph.
