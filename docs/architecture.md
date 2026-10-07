# Architecture

The solution has three projects:

- `SessionPilot.Core` holds INI editing, presets, planning, triggers, transactions, CPU math, cleanup policy, session state, and Ollama schema checks. It does not start processes by itself.
- `SessionPilot.Infrastructure` reads installation candidates, topology buffers, process identity, and the installed power-plan list. It has no write path for Process Lasso.
- `SessionPilot.App` is the WPF shell. It calls Core and Infrastructure. `--check` is a startup argument, not a second executable.

SDK: .NET 10.0.303 from `global.json`. The app targets `net10.0-windows`.

`LiveApplyPolicy.Enabled` is false. `TransactionCoordinator` refuses a path that matches a supplied live candidate. The compiler refuses to emit a writable change while codecs are unverified.

Ollama is optional. The client allows only a loopback HTTP endpoint, sends `keep_alive` 0, and the host stops only a serve process this app started. Inference is refused while a session phase is Active.

There is no database, no elevated helper, and no second writer for a setting Process Lasso already owns.
