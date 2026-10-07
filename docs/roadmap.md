# Roadmap

Shipped in this tree: read-only discovery, rolling diagnostics, guided VR steps, dry-run presets, a session state machine, isolated journals, a single recorded power owner, an explicit Ollama button, opt-in suggestions, and a measurement log that does not claim frame rate. Also shipped: startup that survives a bad journal or user loadout, path redaction, Ollama startup that leaves a busy port alone, language-independent power-plan names, INI edits that keep spacing and refuse an encoding they cannot store, sampling and discovery off the UI thread, a power owner and measurement notes that persist on this machine, and a per-user installer that upgrades in place and leaves the data folder on uninstall.

Still blocked on evidence, and not scheduled as silent automation:

- Live Process Lasso writes, after a version-tagged Export Rules pair and a reload check
- A short-lived elevated helper, only if that codec needs it
- CPU Sets, affinity, Real-time, and High priority
- An actual power-plan switch, after one owner and an export exist
- SteamVR, VRChat, and Virtual Desktop file or API writes
- Force termination
- Headset frame delivery as a measured result

A desktop PresentMon import, if added later, must be labeled desktop presentation, not headset delivery.
