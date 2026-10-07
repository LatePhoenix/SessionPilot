# Presets

Built-in JSON files live in `presets/`. The catalog requires `balanced`. User copies can be stored under `%LOCALAPPDATA%\SessionPilot\loadouts` and must not reuse a built-in id.

| Id | Role |
| --- | --- |
| balanced | Preserve current policy |
| vrchat-steamvr | VRChat with SteamVR |
| vrchat-social | Social VRChat, including Virtual Desktop |
| vrchat-diagnostic | Crowded-instance comparison |
| desktop-gaming | Selected desktop game |
| development-interactive | Editor responsiveness |
| development-build-heavy | Explicit build workers only |
| development-local-ai | Local inference, no model download |
| media-playback | Playback continuity |
| background-batch | Explicit background workers |

Every built-in preset keeps `priorityPolicy` and `cpuPlacementPolicy` at `unchanged`, and `proBalancePolicy` at `preserve`.

The deterministic interpreter maps a SteamVR sentence to `vrchat-steamvr`, Virtual Desktop to `vrchat-social`, and crowded or diagnostic wording to `vrchat-diagnostic`. Two unrelated workloads do not invent a third loadout. Ollama output must match the intent schema and a known loadout id. It cannot carry commands, paths, masks, or termination.
