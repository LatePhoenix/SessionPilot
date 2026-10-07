# SessionPilot refactor audit

Date of this inventory: 2026-10-07. This note describes the tree after the product rename and before the read-only shell. It does not record a Windows profile path or any live configuration values.

## Rename

The solution, projects, namespaces, and assembly names changed from LassoPilot to SessionPilot:

- `SessionPilot.slnx`
- `src/SessionPilot.Core` (`SessionPilot.Core`)
- `src/SessionPilot.Infrastructure` (`SessionPilot.Infrastructure`)
- `src/SessionPilot.App` (`SessionPilot.App`), window title SessionPilot
- `tests/SessionPilot.Tests` (`SessionPilot.Tests`)

Types that name the integrated product stay, including `ProcessLassoInstallation`. Future files owned by this application use `%LOCALAPPDATA%\SessionPilot`. No application settings were stored under a previous name, so there is no user-data migration. The external Process Lasso configuration is not migrated.

The parent folder could not be renamed in this session because the editor denied access to the directory. Project contents were renamed in place. A later folder rename does not change the product.

Baseline before the rename: `dotnet test LassoPilot.slnx` passed 41 tests. The same suite is required to pass as `dotnet test SessionPilot.slnx`.

## Write authority

`LiveApplyPolicy.Enabled` is false. `TransactionCoordinator` refuses a target that matches a supplied live candidate path. `HistoricalRuleParsers.SerializationAllowed` is false. Priority and CPU placement policies in built-in loadouts must stay `unchanged`, and ProBalance policy must stay `preserve`. There is no elevated helper. Isolated copies can be edited only through the existing journaled replacer, and an incomplete journal is described rather than silently rolled back.

## Implemented

- Lossless INI parse and edit in `IniDocument`, including encoding, line endings, unknown lines, and ambiguous keys.
- Dry-run `PlanCompiler` and `CapabilityAssessor`. Live candidate paths are not writable.
- Eight built-in presets: `balanced`, `vrchat-steamvr`, `desktop-gaming`, `development-interactive`, `development-build-heavy`, `development-local-ai`, `media-playback`, `background-batch`.
- Deterministic sentence matching and schema-validated intent JSON. Model output cannot carry commands, paths, masks, or termination.
- Ollama HTTP client with `keep_alive` 0. The host starts `ollama serve` only when the loopback port is down and stops only a process this application started. It does not download Ollama or a model.
- Isolated transactions, content-hash checks, three-way restore, and incomplete-journal discovery.
- `ProcessorTopologyReader` over supplied topology buffers, not a live hardware probe of its own.
- Trigger debounce, dwell, exit grace, and manual override. The engine suggests; it does not apply.
- A short protected-process name list used by planning tests. It is not a process closer.

## Missing

- `SessionPilot.Infrastructure` has no source files.
- The WPF window is an empty template titled SessionPilot.
- No installation-candidate discovery, process inventory, rolling CPU or memory math, cleanup classification, or headless `--check`.
- Presets `vrchat-social` and `vrchat-diagnostic` are not in the catalog yet. `vrchat-steamvr` remains.
- No session state machine, confirmed-path launcher, or ownership separate from already-running applications.
- The transaction engine is not connected to a window. Startup does not scan journals.
- No Windows power-plan discovery and no guided SteamVR, VRChat, or Virtual Desktop cards.
- No prompt page, explicit Ollama button, opt-in trigger surface, or measurement page.
- No README, architecture notes, compatibility notes, or publish folder.
- No GitHub Actions workflow in this commit.

## Deferred adapters

These stay off until a version-tagged export and a reload check exist. Empty observed keys are not that fixture.

- Live Process Lasso writes, including performance-mode membership, Efficiency Mode, CPU priority, affinity, CPU Sets, and watchdogs.
- A second power-plan writer beside Process Lasso.
- SteamVR settings writes. No settings keys are recorded here.
- VRChat configuration writes and any game injection.
- Virtual Desktop telemetry or settings APIs.
- Force termination and process-tree kill.
- CPU Sets, affinity, Real-time, and High priority as product actions.
- An elevated helper.

Performance effect remains `not-measured`. SessionPilot is not affiliated with Bitsum and does not bundle Process Lasso.
