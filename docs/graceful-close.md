# Graceful close

SessionPilot can request a close of one optional application after you approve that exact process. `CloseMainWindow` is a request. A returned success does not mean the process exited. No window, a refusal, and an unsaved-work prompt are results, not errors to override.

The request is sent only after the PID and creation time are read again. A mismatch invalidates the approval. SessionPilot does not call `Kill`, and it does not close a process tree.

Automated tests use a fake closer. They do not target live processes. Continuous integration does not click this action.

## Opt-in manual check

This check is not part of the automated suite and was not run during development.

1. Start an application you can discard, such as a blank Notepad window you opened yourself.
2. Open SessionPilot, go to Diagnostics, and select that one process.
3. Confirm the name, PID, and creation time.
4. Check the approval box and choose Request close.
5. Expect a close request. If Notepad has no document, it may exit on its own. If a save prompt appears, leave it for the user. Do not force the process to end.
6. Do not use this action on Process Lasso, a game, a compositor, or a system process.
