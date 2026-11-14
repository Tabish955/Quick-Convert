# Test fixtures (not committed as binaries unless you pin them)

These tests **skip themselves** when the file is absent, so the suite is green with no fixtures.
Each archive should contain one file, `hello.txt`, with any text.

| File | How to make it |
|---|---|
| `sample-rar4.rar` | WinRAR: Add to archive → format **RAR** with *Compatibility: RAR4* |
| `sample-rar5.rar` | WinRAR: Add to archive → format **RAR** (default, RAR5) |
| `encrypted.zip` | 7-Zip / WinRAR: ZIP with a password |
| `encrypted.7z` | 7-Zip: 7z with a password, "Encrypt file names" ticked |
| `encrypted-headers.rar` | WinRAR: RAR with password + *Encrypt file names* |

Pin each one (version of tool used + SHA256) in `docs/PHASE_4_STATUS.md` once you add them, and keep
them out of git if you prefer to fetch them with a script (decision D6).
