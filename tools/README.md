# tools/

Optional command-line binaries used by the engines. **Nothing binary is committed here.**

```
tools/
├── ffmpeg/    ffmpeg.exe, ffprobe.exe          (Phase 3 — media)
├── 7z/        7za.exe                          (Phase 4 — 7z creation only)
├── pandoc/    pandoc.exe                       (Phase 4 — documents)
└── README.md
```

## Policy

1. **No binaries in Git.** A full FFmpeg + Pandoc bundle is well over 100 MB, would live in history
   forever, and would be pushed to every CI run. `.gitignore` already blocks these folders; only
   `.gitkeep` and this file are tracked.
2. **Fetched by a checksummed script** (`tools/fetch-tools.ps1`, lands in Phase 3) into the layout above.
   Each download is verified against a SHA-256 pinned in that script. If a checksum fails, the build
   fails — never fall back to "download latest".
3. **Bundled only at packaging time.** The Inno Setup script copies `tools/**` into the install
   directory; the dev loop uses whatever the script fetched locally.
4. **Missing tool = clear message, not a crash.** Every engine must resolve its own binary at startup and
   report "FFmpeg not found — run tools/fetch-tools.ps1" through the normal error path.
5. **Licence entry required.** Anything that lands in `tools/` must have a matching entry in
   `THIRD-PARTY-NOTICES.md` in the same PR. CI fails the build if a binary has no notice.

## Which binary and why

| Tool | Used for | Licence note |
|---|---|---|
| `ffmpeg.exe` / `ffprobe.exe` | audio + video transcoding, duration probing | GPL build by default (includes libx264). Decision D1 in `ROADMAP.md`. LGPL build is the alternative, at the cost of H.264 encoding. |
| `7za.exe` | **creating** `.7z` | LGPL + unRAR restriction. We only create 7z with it. |
| SharpCompress (NuGet) | reading zip/tar/gz **and RAR** | MIT, pure managed — this is why no unRAR code ships with us. |
| `pandoc.exe` | documents | GPL 2.0+. |

## Sources (record the exact version + SHA-256 when fetcing)

- FFmpeg: gyan.dev or BtbN Windows builds — pick one and pin it, don't mix.
- 7-Zip: official `7z<version>-extra.7z` (the standalone `7za.exe`).
- Pandoc: official `pandoc-<version>-windows-x86_64.msi` / `.zip`.

Record the URL, version, and hash in `fetch-tools.ps1` **and** in `THIRD-PARTY-NOTICES.md`.
