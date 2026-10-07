# Project overview

SessionPilot prepares a Windows session for a chosen workload. The first workloads are VRChat through SteamVR or Virtual Desktop, plus desktop gaming, development, media, and background batch work.

A loadout is a policy. It is not a Process Lasso profile directory and it is not a promise of higher frame rate.

The window has Dashboard, Diagnostics, Loadouts, Prompt, Plan review, Session, and Measurement. Statuses stay separate: compiled, persisted, governor, effective setting, and performance effect. Performance effect remains `not-measured` until a real measurement import exists.

`SessionPilot.App.exe --check` prints one redacted report and exits. Exit code 0 means the report was written. Exit code 1 means the check failed. It does not sample in a loop.
