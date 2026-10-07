# Manual verification

These checks are for a person at the machine. CI does not perform them, and they were not used to claim a live integration.

## Read-only check

Run `SessionPilot --check` from a terminal. Expect live writes disabled, active configuration assumed no, performance effect not-measured, and sampling loop not started. Confirm the report has no profile path and no configuration values.

## Window

Open the app. Dashboard, Diagnostics, Loadouts, Prompt, Plan review, Session, and Measurement should be reachable from the keyboard. Compile a built-in loadout and confirm every change is not writable. Statuses for persisted, governor, and effective setting stay unverified. Performance stays not-measured.

## Graceful close

Follow `docs/graceful-close.md` only with an application you started and can discard.

## Isolated copy

Point Plan review at a copy of an INI in a folder you own, not at `ProgramData\ProcessLasso`. Approving the current dry-run plan refuses the apply because there are no writable edits. Pointing at the live candidate path must also refuse.

## Power and VR

Recording a power owner must not change the active Windows scheme. VR cards are instructions. Do not expect a settings file to change.

## Ollama

Leave the model box empty and use Interpret. A preset still resolves. Interpret with Ollama only when you want a loopback request, an installed model, and no active session. The prompt is not written to disk.

## Triggers

Leave the opt-in box clear. A signal must not suggest a loadout. With the box checked, a suggestion is text. It does not apply a plan and it does not show a UAC prompt. Choosing a loadout by hand overrides the signal.
