# Architecture

The solution has three projects:

- `SessionPilot.Core` holds INI editing, presets, planning, triggers, transactions, CPU math, cleanup policy, session state, and the Ollama client. The one process it starts is `ollama serve`, through `OllamaServeLauncher`, and only when nothing is listening on the Ollama port. It stops only that process. Other process starts go through interfaces that Infrastructure implements.
- `SessionPilot.Infrastructure` reads installation candidates, processor topology, process samples, and the installed power-plan list. It starts confirmed launches (`ShellProcessStarter`), sends graceful close requests (`LiveWindowCloser`), runs `powercfg /list`, and saves `state.json` (`AppStateStore`). It has no write path for Process Lasso.
- `SessionPilot.App` is the WPF shell. It calls Core and Infrastructure. `--check` is a startup argument, not a second executable.

SDK: .NET 10.0.303 from `global.json`. The app targets `net10.0-windows`.

`LiveApplyPolicy.Enabled` is false. `TransactionCoordinator` refuses a path that matches a supplied live candidate. The compiler refuses to emit a writable change while codecs are unverified.

Ollama is optional. The client allows only a loopback HTTP endpoint, sends `keep_alive` 0, and the host stops only a serve process this app started. Inference is refused while a session phase is Active.

There is no database, no elevated helper, and no second writer for a setting Process Lasso already owns.
