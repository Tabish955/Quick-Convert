# Microsoft Defender SmartScreen & Code Signing Guide

**Version:** 1.0  
**Status:** Adopted (Decision Gate D4)  
**Applies to:** QuickConvert v1.0.0 Setup (`QuickConvert-Setup-1.0.0.exe`) and Standalone Binaries  

---

## 1. Executive Summary & Decision Gate D4

QuickConvert version 1.0.0 is distributed as an **unsigned executable**. Per Decision Gate D4 in `ROADMAP.md`, QuickConvert is free, open source, and offline-only with zero telemetry, data collection, or monetization. Commercial code signing certificates require significant recurring costs ($400–$700/year for Extended Validation) and physical hardware tokens that are disproportionate for an initial open-source release.

Because the binary is unsigned, Microsoft Defender SmartScreen will display an informational security prompt when the installer is launched on a standard Windows 10 or Windows 11 system. This document details the exact prompt, how to safely verify the installer hash, the bypass procedure, and the mechanics of SmartScreen reputation.

---

## 2. The SmartScreen Experience

When launching `QuickConvert-Setup-1.0.0.exe` for the first time, Windows Defender SmartScreen intercepts execution with a modal dialog:

### Exact Dialog Text

```text
Windows protected your PC
Microsoft Defender SmartScreen prevented an unrecognized app from starting. Running this app might put your PC at risk.
```

- **App:** `QuickConvert-Setup-1.0.0.exe` (or `QuickConvert.UI.exe`)
- **Publisher:** `Unknown publisher`

---

## 3. Safe Bypass Instructions

To proceed with installation, the user follows standard Windows bypass steps:

1. On the initial SmartScreen dialog, click the hyperlinked text: **`More info`** (located directly below the warning paragraph).
2. The dialog expands to reveal additional metadata:
   - **App:** `QuickConvert-Setup-1.0.0.exe`
   - **Publisher:** `Unknown publisher`
3. Two action buttons now appear at the bottom right:
   - **`Run anyway`**
   - **`Don't run`**
4. Click **`Run anyway`**.
5. The Inno Setup wizard launches immediately in user mode with **zero elevation / UAC prompts**.

---

## 4. Independent Integrity Verification

Because the binary is unsigned, users should verify binary authenticity using cryptographic hashes rather than certificate signatures. QuickConvert publishes the exact SHA-256 hash for every release.

Before running the installer, open PowerShell and calculate the SHA-256 hash:

```powershell
Get-FileHash -Algorithm SHA256 .\QuickConvert-Setup-1.0.0.exe
```

Compare the resulting hash against the official value published in `docs/PHASE_5_STATUS.md` and the GitHub Release notes:

| File | Size (Bytes) | SHA-256 Checksum |
|---|---|---|
| `QuickConvert-Setup-1.0.0.exe` | 104,175,509 | `416DA81F6E6B2A905D26CD1DDA852F4198D2B4888EA252F20ED2D207E3DDF2C2` |

If the calculated hash matches the published hash, the executable is verified identical to the repository build output and has not been altered in transit.

---

## 5. Technical Mechanics of SmartScreen Reputation

Microsoft Defender SmartScreen uses cloud telemetry and behavioral reputation scoring to evaluate executable files:

1. **Hash Lookup:** When an executable is downloaded or launched, SmartScreen computes its SHA-256 hash and queries Microsoft's application reputation database.
2. **Zero Initial Reputation:** A newly compiled binary hash has zero global download history or telemetry reports. SmartScreen defaults to treating unrecognized binaries as potentially risky until sufficient reputation is established.
3. **Reputation Accumulation:** As users safely download and execute the binary without malware flags or negative telemetry reports from Microsoft Defender Antivirus, reputation accumulates. Once a critical threshold is reached, SmartScreen automatically ceases showing the prompt.
4. **Per-Hash Invalidation:** For unsigned software, any change to the binary (such as a patch or rebuild) produces a distinct SHA-256 hash, resetting reputation to zero.

---

## 6. Evaluation of Code Signing Certificate Options

| Tier | Annual Cost | Identity Verification | SmartScreen Behavior | Decision for QuickConvert |
|---|---|---|---|---|
| **Unsigned** | **$0** | None | Displays SmartScreen "More info" prompt until community reputation builds. | **Selected for v1.0 (Decision D4)**. Standard for free open-source utilities; integrity verified via published SHA-256. |
| **Standard OV** (Organization Validation) | $200 – $350 / year | Business registration or individual identity check | Identifies publisher by name, but **does not grant immediate reputation**. The certificate itself must accumulate reputation over time. | **Rejected for v1.0**. Costs money without eliminating the initial SmartScreen warning. |
| **Extended Validation (EV)** | $400 – $700 / year + HSM costs | Strict legal entity verification, DUNS number, hardware token / cloud HSM | **Immediate SmartScreen reputation**. Eliminates warnings from day one. | **Deferred to v1.1+**. Will be considered once user adoption justifies recurring commercial infrastructure. |

---

## 7. Clean VM Verification Protocol

When verifying the package in a clean, non-domain Windows 10/11 virtual machine:

1. Copy `QuickConvert-Setup-1.0.0.exe` to the virtual machine.
2. Double-click the installer:
   - Observe the Defender SmartScreen prompt.
   - Click **`More info`** -> **`Run anyway`**.
3. Verify that the setup wizard appears without requesting administrative rights (`PrivilegesRequired=lowest`).
4. Complete setup:
   - Install directory is `{localappdata}\Programs\QuickConvert`.
   - Context menu task creates 34 `HKCU\Software\Classes\SystemFileAssociations\<ext>\shell\QuickConvert` entries.
   - Desktop and Start menu shortcuts point to the user-installed executable.
5. Launch `QuickConvert.UI.exe` from Start menu: launches smoothly with zero SmartScreen or UAC prompts.
