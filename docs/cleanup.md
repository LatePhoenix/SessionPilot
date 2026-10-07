# Cleanup

Cleanup is a preview. High CPU does not authorize a close.

Classifications:

- Protected participant, when the caller marks the process as one.
- System or security, from the protected-process list.
- Possible unsaved work, for editors and office applications.
- Unknown, including Python, Node, browsers, and terminals. Those names are not blanket targets.

The optional close control sends `CloseMainWindow` for one approved process after the PID and creation time are read again. A mismatch drops the approval. No main window, a refusal, and a save prompt are results. The request does not mean the process exited. There is no `Kill` and no process-tree close.

Automated tests use a fake closer. See `docs/graceful-close.md` for the opt-in manual check, which is not run in CI.
