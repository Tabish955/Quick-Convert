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

## [in repo] xUnit + Microsoft.NET.Test.Sdk — Apache-2.0 / MIT
Unit and integration test runner. Referenced by test projects only (`QuickConvert.Core.Tests`, `QuickConvert.Engines.Images.Tests`, `QuickConvert.Engines.Media.Tests`), not shipped in binary distribution.

## [planned] MiniExcel — Apache-2.0
CSV/JSON/XLSX. Source: https://github.com/mini-software/MiniExcel
> **ClosedXML is deliberately not used.** It pulls `DocumentFormat.OpenXml` and `SixLabors.Fonts`
> (Six Labors Split Licence: free only under revenue/OSS conditions) plus significant weight.

## [planned] SharpCompress — MIT
zip / tar / gz / RAR **extraction**, pure managed. Source: https://github.com/adamhathcock/sharpcompress
> Using SharpCompress for RAR keeps the 7-Zip unRAR restriction out of our bundle entirely.

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
- **Selected Build Configuration:** Includes `libx264` (H.264/AVC), `libx265` (HEVC), `libvpx` (VP8/VP9), `libopus`, `libmp3lame`, `libvorbis`, and hardware encoder wrappers (`nvenc`, `qsv`, `amf`). Full flags:
  `--enable-gpl --enable-version3 --enable-static --disable-w32threads --disable-autodetect --enable-cairo --enable-fontconfig --enable-iconv --enable-gnutls --enable-libxml2 --enable-gmp --enable-bzlib --enable-lzma --enable-zlib --enable-libsrt --enable-libssh --enable-libzmq --enable-avisynth --enable-sdl2 --enable-libwebp --enable-libx264 --enable-libx265 --enable-libxvid --enable-libaom --enable-libopenjpeg --enable-libvpx --enable-mediafoundation --enable-libass --enable-libfreetype --enable-libfribidi --enable-libharfbuzz --enable-libvidstab --enable-libvmaf --enable-libzimg --enable-amf --enable-cuda-llvm --enable-cuvid --enable-dxva2 --enable-d3d11va --enable-d3d12va --enable-ffnvcodec --enable-libvpl --enable-nvdec --enable-nvenc --enable-vaapi --enable-openal --enable-libgme --enable-libopenmpt --enable-libopencore-amrwb --enable-libmp3lame --enable-libtheora --enable-libvo-amrwbenc --enable-libgsm --enable-libopencore-amrnb --enable-libopus --enable-libspeex --enable-libvorbis --enable-librubberband`
- **Source Offer & Access:** Upstream source corresponding to this binary is available at https://ffmpeg.org/download.html and on GitHub at https://github.com/FFmpeg/FFmpeg (tag `n9.0.2`). Packaging build scripts are archived at https://github.com/GyanD/codexffmpeg.
- **Architectural Isolation:** Quick-Convert executes `ffmpeg.exe` and `ffprobe.exe` exclusively as standalone external processes over process pipes (`ProcessStartInfo.ArgumentList`), maintaining clean architectural and licensing isolation under mere aggregation principles.

### 7-Zip (`7za.exe`) — LGPL 2.1 + unRAR restriction
Used to create 7z archives only. RAR extraction is handled by SharpCompress so we never distribute the
unRAR code path.

### Pandoc — GPL 2.0 or later
Used as a subprocess for document conversion. Same obligations as FFmpeg if bundled: licence text +
source availability.

---

## Compliance checklist per release

- [x] Every `PackageReference` and every file in `tools/` has an entry above.
- [x] No GPL *library* is referenced from our assemblies (a GPL **executable** we shell out to is fine).
- [ ] Licence texts for bundled tools are included in the installer output.
- [x] The source offer link for GPL tools resolves publicly.
- [x] No telemetry, analytics or crash-upload SDK anywhere in the dependency graph.
