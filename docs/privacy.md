# Privacy

SessionPilot is local. It does not upload diagnostics, prompts, or configuration.

`--check` redacts drive paths, UNC paths, and the current account name before printing. Extra tokens passed into the report are redacted the same way. Process command lines are not collected. Journals and isolated copies can still contain whatever was in the file you chose; keep that directory private. The startup report redacts paths in its explanation. Redaction is a filter, not a guarantee that every sensitive string was recognized.

Ollama prompts are sent only to the loopback endpoint you invoke with the button, and they are not written to a prompt log. No model is downloaded.

Exports of a diagnostic are not produced automatically. If you copy the `--check` text, read it first.

Measurement notes and the recorded power owner stay on this machine in the SessionPilot data folder until you clear them. They are not a frame-rate history. Performance effect stays not-measured.
