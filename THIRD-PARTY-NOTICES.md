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

## [planned] Magick.NET — Apache-2.0
Image engine (Phase 2). Wraps ImageMagick, which is under the ImageMagick License (Apache-2.0
compatible). Source: https://github.com/dlemstra/Magick.NET
> Ships large native binaries. Record the unpacked size here when we add it.

## [planned] MiniExcel — Apache-2.0
CSV/JSON/XLSX. Source: https://github.com/mini-software/MiniExcel
> **ClosedXML is deliberately not used.** It pulls `DocumentFormat.OpenXml` and `SixLabors.Fonts`
> (Six Labors Split Licence: free only under revenue/OSS conditions) plus significant weight.

## [planned] SharpCompress — MIT
zip / tar / gz / RAR **extraction**, pure managed. Source: https://github.com/adamhathcock/sharpcompress
> Using SharpCompress for RAR keeps the 7-Zip unRAR restriction out of our bundle entirely.

## [planned] PdfSharp — MIT
PDF output. Source: https://github.com/empira/PDFsharp

## [planned] xUnit + Microsoft.NET.Test.Sdk — Apache-2.0 / MIT
Test-only, not shipped.

---

## Bundled executables (Phase 3+, downloaded by script, not committed)

These are **separate programs invoked as subprocesses** (mere aggregation). Our MIT code is not linked
against them, so their licences do not extend to our source — but we do have to honour them.

### FFmpeg — licence depends on the build (decision D1)
- **Default choice:** GPL build (includes libx264 for H.264/MP4 encoding).
- Obligations: ship the licence text, state the exact build configuration, and make the corresponding
  source available (link to the upstream source tag + our build flags is sufficient).
- Alternative: LGPL build, no x264, encode H.264 via NVENC/QSV/AMF or Windows Media Foundation.
- **Must be recorded here:** version, build URL, whether `--enable-gpl` was used, and where the source
  is offered.

### 7-Zip (`7za.exe`) — LGPL 2.1 + unRAR restriction
Used to create 7z archives only. RAR extraction is handled by SharpCompress so we never distribute the
unRAR code path.

### Pandoc — GPL 2.0 or later
Used as a subprocess for document conversion. Same obligations as FFmpeg if bundled: licence text +
source availability.

---

## Compliance checklist per release

- [ ] Every `PackageReference` and every file in `tools/` has an entry above.
- [ ] No GPL *library* is referenced from our assemblies (a GPL **executable** we shell out to is fine).
- [ ] Licence texts for bundled tools are included in the installer output.
- [ ] The source offer link for GPL tools resolves publicly.
- [ ] No telemetry, analytics or crash-upload SDK anywhere in the dependency graph.
