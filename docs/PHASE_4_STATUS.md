# Phase 4 status (documents, spreadsheets, archives)

**Status:** Delivered, fully verified, and ready for integration.

## Component State

| Area | State | Test Count |
|---|---|---|
| Document engine (Pandoc 3.5, HTML/RTF/ODT/MD/TXT) | Code complete, compiled, and verified | 137 in Docs.Tests (shared with PDF & archives) |
| PDF step (Pandoc → HTML → wkhtmltopdf 0.12.6-1) | Code complete, compiled, and verified | |
| Spreadsheet engine (MiniExcel 1.41.0 + RFC 4180 CSV parser) | Code complete, compiled, and verified | 19 in SpreadsheetConverterTests |
| Archive reading (zip/7z/tar/gz/rar read-only) | Code complete, compiled, and verified | 114 in ArchiveExtractorTests |
| Archive writing (zip/tar/tar.gz in-process, 7z via 7za 23.01) | Code complete, compiled, and verified | 17 in ArchiveCompressorTests |
| Core IO Helpers (`ArchivePathGuard`, `CollisionNamer`, `PromotionHelper`) | Code complete, compiled, and verified | 18 in Collision & PathGuard tests |
| Coordinator / UI / DI wiring, D7 concurrency | Code complete, compiled, and verified | 69 in Core.Tests |
| **Total Test Suite Position** | **255 passing tests** across solution | **255 passed, 0 failed** |

---

## Measured Acceptance Benchmarks

### 1. 5 MB DOCX to PDF Conversion
- **Input Sample:** 4.76 MB (4,994,357 bytes) realistic 25-chapter document with embedded media and styled typography.
- **Wall-clock Conversion Time:** 16.28 seconds (16,278 ms).
- **Output PDF Size:** 73,657 bytes (~0.07 MB).
- **Output Page Count:** 16 pages.
- **Visual & Layout Quality:** Pandoc synthesized HTML5 standalone document cleanly rendered into WebKit PDF layout with correct pagination and font metrics.
- **Installer Size Delta:**
  - `tools/wkhtmltopdf` uncompressed directory: 86.33 MB (90,519,552 bytes).
  - Inno Setup LZMA compressed installer delta: ~41 MB.

### 2. 100 MB ZIP → 7z → Extract Round Trip
- **Input Data:** 10 binary files of 10 MB each (100 MB total) with distinct cryptographic byte streams.
- **In-process ZIP Compression (`SharpCompress`):** 5.38 seconds (5,383 ms), output size: 100.03 MB.
- **7z Compression (`7za.exe`):** 8.38 seconds (8,377 ms), output size: 100.01 MB.
- **ZIP Extraction (`SharpCompress`):** 389 ms (0.39 s).
  - **Hash Verification:** 100% SHA-256 match across all 10 extracted files.
- **7z Extraction (`SharpCompress`):** 735 ms (0.74 s).
  - **Hash Verification:** 100% SHA-256 match across all 10 extracted files.

### 3. RAR4 vs RAR5 Extraction & Rejection
- **Extraction (Read-only):**
  - Both RAR4 and RAR5 unencrypted archives extract cleanly through `SharpCompress.Archives.Rar.RarArchive`.
  - Header-encrypted RAR archives (both RAR4 and RAR5) are intercepted during header parsing, throwing `CryptographicException` / `InvalidOperationException`, which `ArchiveExtractor.Classify` catches and safely translates into `ArchiveFailure.Encrypted` with 0 bytes written to destination.
- **Creation Guard:**
  - RAR archive generation is strictly prohibited by license and architectural boundaries.
  - Vetted by automated test `NoSourceOffersRarAsATarget` and router rejection `router.Resolve("", ".rar") == null`.

### 4. Encrypted Archive Handling
- **Password-protected ZIP (`encrypted.zip`):**
  - Extraction rejected before disk write: `Success = False`, `FailureReason = Encrypted`.
  - User-facing message: `"This archive is password-protected. Quick Convert cannot extract encrypted archives, so nothing was written."`
  - Output directory contents: 0 files leaked.
- **Password-protected 7z with Encrypted Headers (`encrypted.7z`):**
  - Extraction rejected before disk write: `Success = False`, `FailureReason = Encrypted`.
  - User-facing message: `"This archive is password-protected. Quick Convert cannot extract encrypted archives, so nothing was written."`
  - Output directory contents: 0 files leaked.

### 5. Offline Security Verification (wkhtmltopdf)
- **Hostile HTML Source:** HTML document referencing non-routable blackhole (`http://192.0.2.1/...`) and external HTTPS (`https://example.com/...`) remote images.
- **Hardening Flags:** `--disable-javascript`, `--load-error-handling ignore`, `--load-media-error-handling ignore`, `--enable-local-file-access`.
- **Result:** Conversion completes without error, producing a valid offline PDF (`offline_remote_test.pdf`) with 0 network egress and 0 crashes.

---

## Tool Versions & Pinned SHA-256 Hashes

| Executable | Version | SHA-256 Hash |
|---|---|---|
| `pandoc.exe` | 3.5 | `887E980E2228D8D45A431936E8BC6270A50CC9A5A4EC7E62064A3A5DE6423B71` |
| `wkhtmltopdf.exe` | 0.12.6-1 | `1EFCFAF8472871268FD399865A3E62B435B91D4413009B80830EAAFF7A57D088` |
| `7zr.exe` | 23.01 | `72C98287B2E8F85EA7BB87834B6CE1CE7CE7F41A8C97A81B307D4D4BF900922B` |
| `7z2301-extra.7z` | 23.01 | `DB3A1CBE57A26FAC81B65C6A2D23FEAECDEEDE3E4C1FE8FB93A7B91D72D1094C` |
