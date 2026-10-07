# Testing

```powershell
dotnet test SessionPilot.slnx
```

The suite covers the original INI, codec, plan, topology, trigger, transaction, and Ollama lifecycle tests, plus the later checks:

- CPU fraction and logical-core equivalents, with a missing counter left null
- PID reuse and access-denied inventory rows
- Cleanup classification, including runtimes, browsers, terminals, editors, and system processes
- Preset routing for SteamVR, Virtual Desktop, and crowded or diagnostic wording
- Graceful close with a fake closer
- Session transitions, prompt text rejected as arguments, readiness timeout without ownership
- Approval invalidation and incomplete-journal description without rollback
- Power-plan parsing and a refused second writer
- Ollama blocked while a session is Active
- Trigger suggestions that do not apply, and measurement rows that stay not-measured

GitHub Actions runs `dotnet test SessionPilot.slnx` on `windows-latest` for pushes to `main` only.

Graceful close, live Process Lasso writes, power switching, and VR file edits are not exercised against the installed products.
