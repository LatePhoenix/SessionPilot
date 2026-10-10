# SessionPilot

SessionPilot is a Windows workload session manager. It explains resource use, previews a bounded plan, and keeps unsupported changes as guided steps. Process Lasso remains a separate product. SessionPilot does not bundle it and is not affiliated with Bitsum.

Presets, diagnostics, and planning work with no language model installed. Ollama is optional.

For the full picture of the goal, the architecture, and every feature, read [`sessionpilot-overview.md`](sessionpilot-overview.md).

## What it does today

- Discovers Process Lasso read-only: the default executable and `prolasso.ini` locations, the product version, and whether each path is present, missing, or access-denied. A found file is a candidate, not the active configuration.
- Reads processor topology, lists installed Windows power plans, and samples processes for CPU and working set while the Diagnostics page is open.
- Classifies processes for cleanup and can send one graceful close request to one approved process after its PID and creation time are read again.
- Ships ten built-in loadouts (presets) and compiles any of them into a dry-run plan. Every change in a compiled plan is not writable.
- Matches a sentence to a loadout with fixed wording rules. Optionally asks a local Ollama model, whose answer must pass a schema check and agree with the wording rules.
- Walks a session state machine, launches a confirmed path or URI with approved arguments, and records which launches the session owns.
- Binds plan approval to the plan hash and the target file hash, and journals transactions against an isolated copy. Startup describes an incomplete journal without rolling it back.
- Records one power owner and measurement notes on this machine. Performance effect stays `not-measured`.
- Prints a redacted one-pass report with `--check`.

## Requirements

- Windows x64. Live testing has been on Windows 11 only.
- To build: .NET SDK 10.0.303, pinned in `global.json`
- To run the installed MSI: nothing else. The package is self-contained.

## Build and test

```powershell
dotnet test SessionPilot.slnx
dotnet build src/SessionPilot.App/SessionPilot.App.csproj
```

Headless read-only report, one pass, no sampling loop. Exit code 0 means the report was written. Exit code 1 means the check failed.

```powershell
dotnet run --project src/SessionPilot.App/SessionPilot.App.csproj -- --check
```

The built executable is `SessionPilot.App.exe`. It is a Windows application, so its output can interleave with the shell prompt. From cmd, use `start /wait SessionPilot.App.exe --check`. From PowerShell, use `& .\SessionPilot.App.exe --check | Out-String`.

The report redacts filesystem paths and the current account name. It does not treat the first `prolasso.ini` candidate as the active configuration. Live Process Lasso writes stay disabled.

## Self-contained build

```powershell
dotnet publish src/SessionPilot.App/SessionPilot.App.csproj -c Release -r win-x64 --self-contained true -o publish/win-x64
```

That folder is a local build. It does not change Process Lasso. The `publish/` directory is gitignored.

## Installer

Per-user MSI. It does not ask for administrator rights. It installs SessionPilot under the per-user Programs folder, adds a Start Menu shortcut named SessionPilot, and registers an uninstall entry in Settings. A newer build replaces the installed one. A downgrade is refused. Uninstall removes the installed program folder. It leaves `%LOCALAPPDATA%\SessionPilot` in place. It does not install or configure Process Lasso.

The first page, before any files are copied, states that SessionPilot is not associated with Bitsum or Process Lasso, and that Process Lasso is required and is not installed by this package. The app still opens without Process Lasso. Discovery then reports it as not observed.

```powershell
dotnet tool restore
dotnet build installer/SessionPilot.Installer/SessionPilot.Installer.wixproj -c Release
```

The MSI is written to `artifacts/installer/SessionPilot.msi`. That directory is gitignored. The build publishes a self-contained win-x64 app into `artifacts/publish/win-x64/` and packs that folder. The package is unsigned. The version comes from `Directory.Build.props` and is currently 1.0.2.

## Data on this machine

Everything SessionPilot writes goes under `%LOCALAPPDATA%\SessionPilot`:

- `state.json`: the recorded power owner and measurement notes. An unreadable file is moved to `state.json.unreadable` once, and defaults are used.
- `loadouts\`: optional user loadout JSON files. A bad file is skipped and listed on the Dashboard.
- `journals\`: transaction journals and baseline backups for isolated-copy applies.

There is no migration from an older product name. Nothing is uploaded.

## What this build will not do

- Write, restart, or install Process Lasso
- Change any file through a compiled plan. Current plans have no writable changes, so an approved apply to an isolated copy is refused as well.
- Switch a Windows power plan
- Write SteamVR, VRChat, or Virtual Desktop settings
- Force-terminate a process or close a process tree
- Detect a workload by itself. Trigger signals are typed in by hand, and a trigger only suggests.
- Download Ollama or a model
- Claim a measured frame-rate change

See `docs/` for architecture, compatibility, presets, transactions, privacy, telemetry, and the manual checks that are intentionally not part of CI. The original requirements are in `docs/SessionPilot_Project_Specification.txt`, and its section 22 records what this build implements. The remediation plan in `docs/remediation-plan.md` and the live test plan in `docs/live-test-plan.md` are complete.
