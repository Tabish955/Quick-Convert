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

## Command lines

WinRAR and 7-Zip can produce all five without a graphical interface, which keeps the steps repeatable:

```
rar a -ma4 sample-rar4.rar hello.txt
rar a -ma5 sample-rar5.rar hello.txt
rar a -pSECRET -hp encrypted-headers.rar hello.txt
7z a -tzip -pSECRET encrypted.zip hello.txt
7z a -t7z -pSECRET -mhe=on encrypted.7z hello.txt
```

Each file lands well under a kilobyte, so committing them costs nothing in repository size and removes
the skip from five tests. Decision D6 in `docs/PHASE_4_DECISIONS.md` covers the trade-off; if the files
are committed, delete this note and record the SHA-256 of each one in `docs/PHASE_4_STATUS.md`.
