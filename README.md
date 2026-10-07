# SessionPilot

SessionPilot is a Windows workload session manager. It explains resource use, previews a bounded plan, and keeps unsupported changes as guided steps. Process Lasso remains a separate product. SessionPilot does not bundle it and is not affiliated with Bitsum.

Presets, diagnostics, planning, and restoration work with no language model installed.

## Requirements

- Windows
- .NET SDK 10.0.303, pinned in `global.json`

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

The report redacts filesystem paths. It does not treat the first `prolasso.ini` candidate as the active configuration. Live Process Lasso writes stay disabled.

## Self-contained build

```powershell
dotnet publish src/SessionPilot.App/SessionPilot.App.csproj -c Release -r win-x64 --self-contained true -o publish/win-x64
```

That folder is a local build. It does not change Process Lasso. The `publish/` directory is gitignored.

## Installer

Per-user MSI. It does not ask for administrator rights. It installs SessionPilot under the per-user Programs folder, adds a Start Menu shortcut named SessionPilot, and registers an uninstall entry in Settings. A newer build replaces the installed one. A downgrade is refused. Uninstall removes the installed program folder. It leaves `%LOCALAPPDATA%\SessionPilot` in place. It does not install or configure Process Lasso.

The first page, before any files are copied, states that SessionPilot is not associated with Bitsum or Process Lasso, and that Process Lasso is required and is not installed by this package.

```powershell
dotnet tool restore
dotnet build installer/SessionPilot.Installer/SessionPilot.Installer.wixproj -c Release
```

The MSI is written to `artifacts/installer/SessionPilot.msi`. That directory is gitignored. The build publishes a self-contained win-x64 app into `artifacts/publish/win-x64/` and packs that folder. The package is unsigned.

## What this build will not do

- Write, restart, or install Process Lasso
- Switch a Windows power plan
- Write SteamVR, VRChat, or Virtual Desktop settings
- Force-terminate a process or close a process tree
- Download Ollama or a model
- Claim a measured frame-rate change

Application files, if you create any, go under `%LOCALAPPDATA%\SessionPilot`. There is no migration from an older product name.

See `docs/` for architecture, compatibility, presets, transactions, privacy, and the manual checks that are intentionally not part of CI. The remediation plan in `docs/remediation-plan.md` is complete.
