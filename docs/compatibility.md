# Compatibility

SessionPilot runs on Windows with the .NET 10 SDK pinned in `global.json`.

Process Lasso is discovered as a candidate, not assumed. The probed locations are `Program Files\Process Lasso\ProcessLasso.exe`, `Program Files\Process Lasso\ProcessGovernor.exe`, and `ProgramData\ProcessLasso\config\prolasso.ini`. Discovery stops there. It does not search the registry, and a copy installed somewhere else is not a candidate. A missing or access-denied path stays visible. The first present INI is not the active configuration until someone confirms it.

A read-only `--check` on one development machine reported product version 18.4.0.48. That is an observed file version, not a certification, and it does not verify rule serialization or governor reload.

Hardware comes from `GetLogicalProcessorInformationEx` parsed by `ProcessorTopologyReader`. If that read fails, the hardware summary is unavailable rather than zero. CPU set buffers are not collected. GPU engine counters are unavailable.

Power plans are listed with `powercfg /list` when that command returns. Listing does not change the active scheme.

SteamVR settings were not readable for this build, so no settings keys are stored. VRChat guidance is limited to steps in the public configuration window: avatar rank, max shown avatars, hide-beyond distance, graphics profile, MSAA, and mirrors. Virtual Desktop guidance is to record what the overlay shows.

Launching tracks the process Windows starts. Some executables are launcher stubs: on Windows 11, `notepad.exe` hands off to the Store Notepad and the stub exits, and game launchers often do the same. SessionPilot then owns the stub, not the window that appears. A graceful close request on a process with no main window is refused rather than sent.

The Ollama probe waits up to 5 seconds, because Windows takes about 2 seconds to report a refused loopback connection. Installed model names are read from the Ollama `manifests` folder (`OLLAMA_MODELS`, or `%USERPROFILE%\.ollama\models`). Only names are read.
