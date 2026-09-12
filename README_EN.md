<div align="center">

# WinHomestead

**A setup tool for fresh Windows 11 machines — every tweak listed, explained, and reversible**

[简体中文](README.md) · **English**

[![release](https://img.shields.io/github/v/release/CookPiu/WinHomestead?style=flat-square)](https://github.com/CookPiu/WinHomestead/releases/latest)
[![downloads](https://img.shields.io/github/downloads/CookPiu/WinHomestead/total?style=flat-square)](https://github.com/CookPiu/WinHomestead/releases)
[![build](https://img.shields.io/github/actions/workflow/status/CookPiu/WinHomestead/build.yml?branch=main&style=flat-square)](https://github.com/CookPiu/WinHomestead/actions/workflows/build.yml)
[![license](https://img.shields.io/github/license/CookPiu/WinHomestead?style=flat-square)](LICENSE)
[![platform](https://img.shields.io/badge/Windows-11%2022H2%2B-0078D4?style=flat-square&logo=windows11&logoColor=white)](#requirements)

</div>

Setting up a new Windows 11 machine means hunting through Settings, Folder Options, the registry, IME properties and Disk Management. It takes an afternoon, and you always forget something.

WinHomestead collects all of it into one list: **UI and interaction, Chinese IME, disks and paths, display and power, startup items, disk cleanup, security checks, software planning**. It probes the machine on launch, then shows every applicable item with **what it is now and what it will become**. Click an item, it runs, the list refreshes in place.

No questionnaire, no "optimize everything" button, no black box. Every item states what it changes, why it's worth changing, and what the side effects are.

> [!IMPORTANT]
> Early release (0.2.x). The published binary is **not code-signed**, so SmartScreen will warn on first run. The tool modifies system settings — every item is reversible and a restore point is created before the first change, but try it on a VM or a machine you don't care about first.

> **A note on language:** the interface is currently Chinese-only. Several features target Chinese users specifically (Microsoft Pinyin IME switches). English UI is [planned](#roadmap); the rest of the tool works the same regardless of your system language.

## Download

Grab both files from [Releases](https://github.com/CookPiu/WinHomestead/releases/latest), **keep them in the same folder**, and run the exe:

| File | Why |
|---|---|
| `WinHomestead.exe` | Single file, all dependencies embedded |
| `WinHomestead.exe.config` | Binding redirects and runtime declaration — it won't start without this |

When SmartScreen shows the blue box, click **More info** → **Run anyway**. If you'd rather not, [build it yourself](#building).

### Requirements

- Windows 11 22H2 or later
- An administrator account (the app elevates once at startup)
- No .NET install needed — it runs on the .NET Framework 4.8 that ships with Windows

## What it does

**UI and interaction** — the first batch you change after a clean install
- Show file extensions and hidden files, open File Explorer to This PC, show This PC on the desktop, add "End task" to the taskbar context menu
- Turn off mouse acceleration, sticky/filter keys, and the Alt+Shift language hotkey — the usual sources of accidental triggers
- Full path in the title bar, never combine taskbar buttons, seconds on the clock, keep recent items out of Quick Access, disable Aero Shake
- Taskbar alignment, search box style, Start menu layout, classic context menu, dark mode (all marked optional — no opinion imposed)
- A set of promo switches: lock screen Spotlight, Start menu suggestions, silent app installs, web results in search, advertising ID, and sync provider notifications in File Explorer

**Chinese IME** — three Microsoft Pinyin switches that take forever to find
- Start in English mode, disable the Ctrl+. punctuation toggle, disable single-Shift language switching

**Disks and paths**
- Detect the data drive; on a single unpartitioned disk, suggest a split — and offer one-click shrink-C-and-create-D only when every precondition holds
- Create a directory skeleton on the data drive and move Documents / Downloads / Pictures / Videos / Music and TEMP there
- When OneDrive has taken over the Desktop, it doesn't fight it — it tells you where to turn that off

**Development environment** — pave the road *before* installing tools
- Point the caches of pip, uv, npm, pnpm, Yarn, Gradle, Maven, NuGet, vcpkg, Cargo, Go, Flutter, Conda, Hugging Face and Ollama at the data drive in one go
- Variables you already set are left alone, and tools already installed are skipped — moving those is relocation, not setup
- Dev Drive advice: detects whether the data drive is ReFS and proposes how to create one from unallocated space. Advice only — it never formats a volume

**Display and power**
- Raise the refresh rate to the highest the current resolution supports — high-refresh panels shipping at 60 Hz is the norm on new machines
- Turn off Fast Startup so "shut down" actually means shut down (dual-boot volume locking and stuck peripherals usually trace back to it)
- Later screen-off and sleep on AC (15 / 60 min); the battery side keeps Windows' own power-saving defaults
- Hardware-accelerated GPU scheduling (HAGS), shown only when the GPU and driver support it

**Startup items**
- Lists entries from the Run keys and the Startup folder, with a switch on each
- Only writes `StartupApproved` — the same mechanism Task Manager uses — so the program itself stays put and can be re-enabled any time

**Disk cleanup**
- Stale temp files, hibernation file (desktops), Storage Sense

**Security and status checks** (eight read-only items — findings and a jump button, nothing is changed for you)
- BitLocker recovery key, multiple AV products installed, patch age, restore point footprint
- OEM bloatware, battery health, default apps, region and time zone

**Software planning**
- Official download page links plus a planned install path per category, one click to copy
- No downloads, no silent installs, no bundled installers

**Machine info and run log**
- "This PC" lists hardware and per-volume capacity with exact values, never rounded
- Per-session results and manual follow-ups export to an HTML report

## What it deliberately doesn't do

These are omissions by design, not gaps:

- ❌ No uninstalling Microsoft's built-in apps, no disabling services, no touching Windows Update
- ❌ No changes to UAC, SmartScreen, or memory integrity
- ❌ No DNS, hosts file, or IPv6 tinkering
- ❌ No network access at all — no update checks, no telemetry, nothing uploaded
- ❌ No junk-file scanning; this is for machines that are brand new

The test is "can you feel the effect, and is the risk symmetric?" Anything with unclear benefit but a real chance of leaving a mess is out — which means this is **not a performance tool**; it won't raise your benchmark scores. What it does is get a new machine configured properly in one pass. Every judgment call is written down in the [scope document](docs/01-范围清单.md) (Chinese).

## Why you can trust it with your system

- **Restore point first** — created automatically before the first change of each session
- **Every write is journaled** — the previous value goes to `%ProgramData%\WinHomestead\journal-*.jsonl` before anything changes
- **Automatic rollback** — a failed item is reverted entry by entry from the journal, never left half-applied
- **Irreversible actions are singled out** — partition operations require explicit confirmation, and no existing partition is ever deleted, moved, or merged
- **Managed devices are recognized** — under MDM or a domain, HKLM-touching items are skipped with the reason shown
- **Probing has no side effects** — the Detect phase only reads the registry and WMI

## FAQ

<details>
<summary>How is this different from one-click optimizer / booster tools?</summary>

Different goal. Those sell "turn things off for performance" — disabling services, killing scheduled tasks, cleaning the registry, removing preinstalled apps. WinHomestead does none of that. It targets a machine that **hasn't been used yet**, and the problem it solves is "the settings worth changing are scattered across a dozen places and take an afternoon to work through."

Three concrete differences: **visible per item** — each one shows the before and after value plus side effects, and there's no select-all; **reversible** — original values go to a journal and failures roll back; **conservative** — Microsoft's own services and apps are never touched.
</details>

<details>
<summary>SmartScreen says "unknown publisher" — should I trust it?</summary>

Code-signing certificates cost money and this project hasn't bought one. You can build from source (see below), or look at the artifacts built automatically on every commit under [Actions](https://github.com/CookPiu/WinHomestead/actions) — the build is public.
</details>

<details>
<summary>Can I run it on a machine I've been using for years?</summary>

Yes, but it isn't designed for that. Anything already done shows as satisfied and anything that can't apply shows as not applicable with a reason, so it won't make a mess. But it **doesn't do cleanup**: no AppData scanning, no large-file hunting, no relocating tools you've already installed.
</details>

<details>
<summary>Why won't it remove the preinstalled apps?</summary>

Microsoft's own apps and services are kept, all of them. Removing Appx packages and disabling services behaves inconsistently across builds and is hard to undo when it goes wrong; the payoff (a few hundred MB, a few fewer icons) doesn't justify that. OEM bloatware is listed in the checks page — you decide what to uninstall, and you can stop it from auto-starting on the Startup items page.
</details>

<details>
<summary>Does it upload my data?</summary>

No. The tool never goes online: no update checks, no usage reporting, no telemetry. The only external process it ever spawns is for the battery health check — when firmware won't report design capacity, it runs `powercfg /batteryreport` once and deletes the temp file after reading it.
</details>

<details>
<summary>Can changes be undone?</summary>

Registry and environment variable changes can, with original values kept in the journal. Partition operations can't — those items are never pre-selected and require explicit confirmation.
</details>

## Building

Requires the .NET 10 SDK (for building only; the output runs on the .NET Framework 4.8 that ships with Windows).

```powershell
git clone https://github.com/CookPiu/WinHomestead.git
cd WinHomestead

.\build\build.ps1          # Debug build + unit tests
.\build\publish.ps1        # Release single exe (Costura-embedded) + size check, output in artifacts\
.\build\publish.ps1 -Thumbprint <cert thumbprint>   # same, signed (SHA256 + RFC3161 timestamp)
```

If the SDK isn't on your PATH, point the `$sdk` variable at the top of `build\build.ps1` at it.

`build\inspect-machine.ps1` is a read-only collector that prints the relevant settings of the current machine to the console, useful for cross-checking what the tool detected. It changes nothing and sends nothing anywhere — but the output does contain machine configuration, so give it a look before pasting it elsewhere.

## Project layout

```
src/WinHomestead.Core     models, ITask, Planner, TaskRunner, logging, persistence (no WPF dependency)
src/WinHomestead.Native   registry, environment variables, known folders, Explorer, storage, restore points, probing
src/WinHomestead.Tasks    task implementations and the task catalog
src/WinHomestead.App      WPF interface (WPF-UI)
tests/                    xUnit unit tests
build/                    build scripts and the read-only collector
```

Dependencies flow App → Tasks → Core and App → Native → Core. **Tasks never references Native**, reaching the system only through Core's interfaces — which is why every task is unit-testable against fakes without touching a real registry.

Every task implements four stages: `Detect` (works out the current state without side effects), `Apply`, `Verify`, `Rollback`. To add one, implement `ITask` and register it in `TaskCatalog`.

## Roadmap

- English UI (the interface is Chinese-only today)
- Code signing, so SmartScreen stops warning
- A screenshot in this README

## Documentation

The design documents are written in Chinese:

| Document | Contents |
|---|---|
| [01 Scope](docs/01-范围清单.md) | What's in, what's out, and the reasoning per item |
| [02 Use cases](docs/02-用例与用户流程.md) | Main flow and edge cases for each use case |
| [03 Technical design](docs/03-技术设计.md) | Layering, execution engine, journaling, native wrappers |
| [04 Release checklist](docs/04-发布检查清单.md) | Automated gates and manual walkthrough before shipping |
| [05 Research notes](docs/05-开荒操作资料汇编.md) | External sources and why each finding was included or rejected |

## Contributing

Issues and PRs welcome. Before you submit:

- A new task must implement Detect / Apply / Verify / Rollback, with writes going through the journaling wrappers on `TaskContext`
- Update the corresponding document under `docs/` when scope or flow changes
- Run `.\build\build.ps1` — zero warnings, zero errors, all tests green
- Tasks that uninstall preinstalled apps, disable services, or turn off updates won't be accepted; see [what it deliberately doesn't do](#what-it-deliberately-doesnt-do)

## License

[MIT](LICENSE)
