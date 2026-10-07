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

Headless read-only report, one pass, no sampling loop:

```powershell
dotnet run --project src/SessionPilot.App/SessionPilot.App.csproj -- --check
```

The report redacts filesystem paths. It does not treat the first `prolasso.ini` candidate as the active configuration. Live Process Lasso writes stay disabled.

## Self-contained build

```powershell
dotnet publish src/SessionPilot.App/SessionPilot.App.csproj -c Release -r win-x64 --self-contained true -o publish/win-x64
```

That folder is a local build. It is not an installer and it does not change Process Lasso. The `publish/` directory is gitignored.

## What this build will not do

- Write, restart, or install Process Lasso
- Switch a Windows power plan
- Write SteamVR, VRChat, or Virtual Desktop settings
- Force-terminate a process or close a process tree
- Download Ollama or a model
- Claim a measured frame-rate change

Application files, if you create any, go under `%LOCALAPPDATA%\SessionPilot`. There is no migration from an older product name.

See `docs/` for architecture, compatibility, presets, transactions, privacy, and the manual checks that are intentionally not part of CI.
