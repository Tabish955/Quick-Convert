# tools/

Optional command-line binaries used by the engines. **Nothing binary is committed here.**

```
tools/
├── ffmpeg/     ffmpeg.exe, ffprobe.exe         (Phase 3 — media)
├── pandoc/     pandoc.exe                      (Phase 4 — documents)
├── wkhtmltopdf/ wkhtmltopdf.exe and its Qt DLLs (Phase 4 — PDF output)
├── 7z/         7za.exe                         (Phase 4 — 7z creation only)
├── spike/      throwaway scripts, not shipped
├── Get-*.ps1   one downloader per tool, each with a pinned SHA-256
└── README.md
```

## Policy

1. **No binaries in Git.** A full FFmpeg + Pandoc bundle is well over 100 MB, would live in history
   forever, and would be pushed to every CI run. `.gitignore` already blocks these folders; only
   `.gitkeep` and this file are tracked.
2. **Fetched by a checksummed script**, one per tool: `Get-Ffmpeg.ps1`, `Get-Pandoc.ps1`,
   `Get-Wkhtmltopdf.ps1` and `Get-7za.ps1`. Each download is verified against a SHA-256 pinned in that
   script. If a checksum fails, the build fails. Never fall back to "download latest".
3. **Bundled only at packaging time.** The Inno Setup script copies `tools/**` into the install
   directory; the dev loop uses whatever the script fetched locally.
4. **Missing tool = clear message, not a crash.** Every engine resolves its own binary and reports
   "FFmpeg not found, run tools/Get-Ffmpeg.ps1" through the normal error path.
5. **Licence entry required.** Anything that lands in `tools/` must have a matching entry in
   `THIRD-PARTY-NOTICES.md` in the same PR. CI fails the build if a binary has no notice.

## Which binary and why

| Tool | Used for | Licence note |
|---|---|---|
| `ffmpeg.exe` / `ffprobe.exe` | audio + video transcoding, duration probing | GPL build by default (includes libx264). Decision D1 in `ROADMAP.md`. LGPL build is the alternative, at the cost of H.264 encoding. |
| `7za.exe` | **creating** `.7z` | LGPL + unRAR restriction. We only create 7z with it. |
| SharpCompress (NuGet) | reading zip/tar/gz **and RAR** | MIT, pure managed — this is why no unRAR code ships with us. |
| `pandoc.exe` | documents | GPL 2.0+. |
| `wkhtmltopdf.exe` | rendering generated HTML to PDF | LGPL-3.0, and it embeds Qt. The executable and its DLLs ship together; see `docs/PHASE_4_DECISIONS.md` D5. |

## Sources (record the exact version + SHA-256 when fetcing)

- FFmpeg: gyan.dev or BtbN Windows builds — pick one and pin it, don't mix.
- 7-Zip: official `7z<version>-extra.7z` (the standalone `7za.exe`).
- Pandoc: official `pandoc-<version>-windows-x86_64.zip`.
- wkhtmltopdf: the patched Qt build from `wkhtmltopdf/packaging` releases, which is self contained.

Record the URL, version, and hash in the matching `Get-*.ps1` **and** in `THIRD-PARTY-NOTICES.md`.
