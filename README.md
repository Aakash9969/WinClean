<div align="center">

# WinClean

**A safe, fast, personal junk cleaner for Windows 10/11.**

Scan first. Review everything. Delete only what you approve.

![Platform](https://img.shields.io/badge/platform-Windows%2010%20%2F%2011-blue)
![.NET](https://img.shields.io/badge/.NET-8.0-512BD4)
![Tests](https://img.shields.io/badge/tests-83%20passing-brightgreen)
![License](https://img.shields.io/badge/license-MIT-green)

</div>

---

## Overview

WinClean is a rules-driven system cleaner built with C#, .NET 8 and WPF (MVVM). It is made for
personal use on your own machines, and it keeps one simple contract:

> **Scanning never deletes.** Deletion happens only after you review the results, check exactly
> what you want cleaned, and confirm an explicit dialog.

- **Rules-driven** — every cleanable target lives in `rules.json`, never in code. Adding a new
  target is a JSON edit.
- **No telemetry, no network calls, no paid libraries.**
- **No registry cleaning** — by design.
- **Local first** — settings and clean history are plain JSON in `%LOCALAPPDATA%\WinClean`, and
  logs are rolling text files you can read with Notepad.

## Features

- **Scanning** — async and cancellable, with live progress and sizes/file counts per rule and per
  category.
- **Review** — a category tree with tri-state checkboxes, color-coded risk badges, `Admin` badges
  for rules that need elevation, scan notes, and live *selected* totals.
- **Cleaning** — runs only on the rules you checked, after a confirmation dialog that lists
  exactly what will be deleted. Locked or in-use files are skipped and reported with a reason —
  never force-deleted.
- **Summary** — space freed, files removed, and every skipped file with its reason.
- **History** — every clean is recorded locally: date, bytes freed, files removed/skipped, and a
  per-rule breakdown.
- **Settings** — dark/light theme and default-enabled rules, applied instantly and persisted.
- **Elevation on demand** — administrator rights are requested only when an enabled rule needs
  them, never at startup. WinClean relaunches elevated and picks up right where you left off.
- **Memory optimizer** — reclaim RAM from running applications by trimming working sets; nothing
  is terminated, and critical system processes are skipped automatically.
- **Audit trail** — rolling Serilog logs record every deletion and every skip.

## Safety model

Safety is the highest priority in WinClean. These rules are enforced in code, not by convention:

| Layer | Guarantee | Enforced by |
|---|---|---|
| Scan-only phase | Scanning never deletes anything | `Scanner` |
| Explicit approval | Deletion runs only on checked rules, only after the confirm dialog | Clean flow |
| Whitelist | A file's full canonical path must lie strictly inside a root declared by an enabled rule | `PathValidator` |
| Hard blocklist | Protected locations are refused **even if a rule points at them** | `PathValidator` |
| No symlinks / junctions | Reparse points are never followed during traversal and never deleted; anything that cannot be verified is refused | `PathValidator`, `Scanner` |
| Delete-time re-validation | Every file is re-validated immediately before deletion (it may have changed since the scan) | `Cleaner` |
| Skip, never force | Locked or in-use files are skipped and listed with a reason | `Cleaner` |
| Recycle Bin via Shell API | The bin is emptied only through the Windows Shell API, never by deleting its on-disk folders | `ShellRecycleBinEmptier` |
| Elevation on demand | Admin rights are requested only when an enabled rule needs them | `ElevationService` |
| No registry cleaning | WinClean never writes to the registry (it reads one shell-folder key to locate your Downloads folder) | — |
| Audit trail | Every deletion and every skip is written to the log | `WinCleanLogger` |

**Protected locations** (hard blocklist):

| Location | Protection |
|---|---|
| User profile root (`C:\Users\<you>`) | Files *directly* inside it (`NTUSER.DAT` etc.) are protected; rule-declared folders deeper within stay cleanable |
| `C:\Windows` root | Files *directly* inside it (`MEMORY.DMP` etc.) are protected; rule-declared subfolders such as `Prefetch` stay cleanable |
| Documents, Desktop, Pictures, Videos, Music, Downloads | Everything beneath is protected |
| `C:\Windows\System32` | Everything beneath is protected |
| Program Files, Program Files (x86) | Everything beneath is protected |

The two *root-level* protections are what make `%WINDIR%\Prefetch` or `%WINDIR%\Temp` legitimate
cleaning targets while `C:\Windows\MEMORY.DMP` and `NTUSER.DAT` remain untouchable. The blocklist
also matches Windows 8.3 short-name aliases (`C:\PROGRA~1`), so alias spelling cannot bypass it.

Browser rules target **cache folders only** — cookies, passwords, history, sessions and
extensions are never touched.

## Rules

Cleanable targets are data. The bundled `rules.json` ships next to the executable, and an
optional user file at `%LOCALAPPDATA%\WinClean\rules.json` **fully replaces** it — so you can
add targets without touching the install folder:

```json
{
  "$schema": "./rules.schema.json",
  "rules": [
    {
      "id": "user-temp",
      "name": "User temporary files",
      "category": "System",
      "description": "Leftovers in your personal temp folder (%TEMP%).",
      "riskLevel": "Safe",
      "requiresAdmin": false,
      "paths": [ "%TEMP%" ],
      "filePattern": "*",
      "recursive": true
    },
    {
      "id": "chrome-cache",
      "name": "Google Chrome caches",
      "category": "Browsers",
      "description": "Chrome's temporary caches for all local browser profiles.",
      "riskLevel": "Safe",
      "requiresAdmin": false,
      "paths": [
        "%LOCALAPPDATA%\\Google\\Chrome\\User Data\\*\\Cache",
        "%LOCALAPPDATA%\\Google\\Chrome\\User Data\\*\\Code Cache"
      ],
      "filePattern": "*",
      "recursive": true
    }
  ]
}
```

| Field | Meaning |
|---|---|
| `id` | Unique, stable identifier (used by settings and history) |
| `name`, `category`, `description` | Shown in the review tree |
| `riskLevel` | `Safe`, `Moderate` or `Advanced` — drives the risk badge |
| `requiresAdmin` | When true, scanning offers an elevation restart |
| `paths` | Absolute roots; environment variables are expanded; a `*` segment matches any single folder name (one per browser profile) |
| `filePattern` | Plain file-name pattern such as `*.pf`. Path separators, `..` and drive letters are rejected at load time |
| `recursive` | Also match files in subfolders of each root |
| `specialTarget` | Built-in handler for targets that are not path-based — currently `RecycleBin` (Shell API) |

The full schema is in [`src/WinClean.Core/rules.schema.json`](src/WinClean.Core/rules.schema.json).
Bundled rules: user temp, Windows temp, Windows Update download cache, Prefetch, thumbnail cache,
crash dumps, Windows error reports, Recycle Bin, and the caches of Chrome, Edge and Firefox.

## Build instructions

Requirements:

- Windows 10/11 (x64, or ARM64 via emulation)
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- [Inno Setup 6.3+](https://jrsoftware.org/isinfo.php) — only for building the installer

```powershell
git clone https://github.com/Aakash9969/WinClean.git
cd winclean

dotnet build                                       # warnings are errors in this repo
dotnet test                                        # 83 tests, including the full safety suite

dotnet run --project src/WinClean.App              # run the app

powershell -File tools\publish.ps1                  # self-contained single-file exe -> publish\win-x64
& "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe" installer\WinClean.iss
# -> publish\installer\WinClean-Setup-<version>.exe
```

The published `WinClean.exe` is a single self-contained file (about 69 MB) that includes the
.NET runtime — no installation is needed on the target machine.

### Where WinClean keeps its data

| Path | Contents |
|---|---|
| `%LOCALAPPDATA%\WinClean\logs\` | Rolling daily logs (14 days, 10 MB cap) — every deletion and every skip |
| `%LOCALAPPDATA%\WinClean\history.json` | Past cleans |
| `%LOCALAPPDATA%\WinClean\settings.json` | Theme + disabled rules |
| `%LOCALAPPDATA%\WinClean\rules.json` | Optional rules override (replaces the bundled file) |

Uninstalling WinClean never deletes this data.

## Project structure

```
winclean/
├─ WinClean.sln
├─ src/WinClean.Core/               # business logic — no UI code
│  ├─ Models/                       #   Rule, RiskLevel, ScanFinding
│  ├─ Rules/                        #   rules.json loader + validation
│  ├─ Paths/                        #   PathValidator — whitelist, blocklist, symlink checks
│  ├─ Scanning/                     #   async, cancellable scanner
│  ├─ Cleaning/                     #   cleaner engine (dry-run capable, delete-time validation)
│  ├─ Special/                      #   Recycle Bin Shell API integration
│  ├─ Storage/                      #   settings + history (atomic JSON)
│  ├─ Security/                     #   elevation checks
│  └─ Logging/                      #   Serilog setup
├─ src/WinClean.App/                # WPF UI — MVVM only, no business logic in code-behind
│  ├─ ViewModels/  Views/  Services/  Theme/  Converters/
├─ tests/WinClean.Tests/            # xUnit tests for Core (temp-folder fixtures only)
├─ installer/WinClean.iss           # Inno Setup installer script
└─ tools/                           # publish.ps1, make-icon.ps1
```

## Screenshots

> Screenshots coming soon — drop PNGs into `docs/screenshots/` and embed them here.

| View | Planned file |
|---|---|
| Cleaner — review tree with risk badges | `docs/screenshots/cleaner.png` *(TODO)* |
| Clean confirmation dialog | `docs/screenshots/confirm.png` *(TODO)* |
| Summary with skipped files and reasons | `docs/screenshots/summary.png` *(TODO)* |
| Settings — theme and default-enabled rules | `docs/screenshots/settings.png` *(TODO)* |

## Roadmap

Ideas, not promises:

- [ ] Prune empty directories left behind after cleaning
- [ ] A *preview (dry run)* button in the UI — the cleaner engine already supports dry runs and
      is tested against them
- [ ] Editing rules from the Settings page
- [ ] Native ARM64 build
- [ ] winget distribution

## Contributing

This is a personal project, but issues and pull requests are welcome. The non-negotiables:

1. **Safety first** — any change touching `PathValidator`, `Cleaner` or the blocklist must come
   with new unit tests (path traversal, symlink/junction escape, protected roots).
2. Keep `dotnet build` warning-free — warnings are errors in this repo.
3. Run `dotnet test` before every pull request.
4. No telemetry, no network calls, no paid dependencies.
5. Views contain no business logic; Core contains no UI code.

## Disclaimer

WinClean deletes files to free space. It is provided as-is, without warranty of any kind — see
the [LICENSE](LICENSE). You are responsible for reviewing what will be deleted before you confirm
a clean; locked and protected files are skipped automatically, but no software is perfect. Use
WinClean on your own machines, keep backups of anything you care about, and read the review
screen before pressing *Clean now*.

WinClean is not affiliated with Microsoft. Windows is a trademark of Microsoft Corporation.

## License

Released under the [MIT License](LICENSE).
