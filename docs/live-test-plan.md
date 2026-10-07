# Live device test plan

Source: live test of the 1.0.1 installer on the owner's Windows 11 machine, 2026-10-07. The installed build was driven through Windows UI Automation. Process Lasso 18.4 was present and read only. The live `prolasso.ini` hash was checked before and after and did not change.

## What passed

- Install, uninstall, 1.0.0 → 1.0.1 upgrade (one product left), and the downgrade refusal. Uninstall removes the program folder, the shortcut, and `HKCU\Software\SessionPilot`, and keeps `%LOCALAPPDATA%\SessionPilot`.
- The new icon is used by the shortcut, the uninstall entry, the executable, and the title bar.
- `--check` returns 0 in about 1 second, including while the window is open.
- Startup discovery takes about 150 ms. Process Lasso, topology, and power plans are found read-only.
- Power owner record, refusal of a second owner, clear, and persistence across a restart.
- Sampling runs every 2 seconds at about 10 ms per sample and stops off the Diagnostics page.
- Trigger opt-out, the 20-second dwell completing into `suggest`, manual override, and clearing it.
- Deterministic prompts, including plurals.
- Plan approval binding and the dry-run refusal. Nothing was written to the isolated copy or the live file.
- Session phases from Idle to Completed, with each stop message.
- Launch with an argument containing spaces. The path reached the process quoted correctly.
- Graceful close: a process with no window is refused, and a window close request closed Notepad.
- A corrupt `state.json`, user loadout, and journal are each skipped and listed, and startup continues.

## Findings

| ID | Severity | Finding |
|---|---|---|
| L1 | High | Ollama is never started. A refused connection to `127.0.0.1` takes about 2.1 s on Windows, but the probe gives up after 2 s and reports "timed out". |
| L2 | High | The model is not told the loadout ids, and `loadoutId` is free text. `phi3` answered `"12345"` and invented three game names. |
| L3 | Medium | The 60-second request timeout expired on a cold `phi3` load. A direct call took 36 s without an id list and 6 s with one. |
| L4 | Low | The model box starts empty and the error does not say which models are installed. |
| L5 | Low | Apply before any approval says "The plan or file changed. Approval was invalidated." |
| L6 | Low | An unknown loadout id in the trigger signals says "No workload signal." |
| L7 | Low | Skipped-file lines have no separator: `corrupt.json It was not opened or rolled back.` |
| L8 | Low | A corrupt `state.json` is overwritten by the next save, and its contents are lost without a copy. |
| L9 | Low | The title bar is light while the window is dark. |
| L10 | Low | Every install logs `WixRemoveFoldersEx: Error 0x80070057: Missing folder property`. It is harmless, but it reads as a failure. |
| L11 | Info | `notepad.exe` and other launcher stubs hand off to another process and exit. SessionPilot then owns the stub, not the window. |
| L12 | Info | The install is 140 MB. WPF satellite resource folders for 13 languages are included although the app is English only. |
| L13 | Low | Interpretation warnings are never shown. The "termination and Real-time priority are not available" warning and dropped-name notes stay hidden. Found while re-testing L2. |

## Plan

Branch: `fix/live-test-findings`. One PR.

- [x] **L1** Raise the probe limit to 5 s so a refused loopback connection is seen as down. Test: a fake handler that throws `HttpRequestException` after 2.5 s leads to a launch.
- [x] **L2** Pass the catalog to the Ollama client. The system prompt lists each loadout id with its summary, and the schema's `loadoutId` becomes an `enum` of those ids. Drop requested applications that do not appear in the prompt text. When the deterministic interpreter matched a different loadout, report both and do not compile. Tests: the request body carries the enum and the id list; an invented application is dropped; a disagreement does not compile.
- [x] **L3** Raise the default request timeout to 120 s. Update the existing default test.
- [x] **L4** When the model box is empty, list the installed model names read from the Ollama manifests folder (`OLLAMA_MODELS` or `%USERPROFILE%\.ollama\models`). Names only. Test the name parsing on a synthetic folder.
- [x] **L5** Before any approval, say "Approve the plan before applying. Nothing was written."
- [x] **L6** When signals are present but none is a known loadout, say "No known loadout in the signals."
- [x] **L7** Use `name: reason` for every skipped file line.
- [x] **L8** On a corrupt or unreadable `state.json`, move it to `state.json.unreadable` (replacing an older one) so the next save cannot overwrite it, and say so once. Test it.
- [x] **L9** Ask DWM for a dark title bar (`DWMWA_USE_IMMERSIVE_DARK_MODE`). Ignore failure on older builds.
- [x] **L10** Find a way to stop the `RemoveFolderEx` error on first install without losing uninstall cleanup. Verify with an install, uninstall, and log check.
- [x] **L11** Document the launcher-stub limit in `docs/compatibility.md`. No code change.
- [x] **L12** Set `SatelliteResourceLanguages` to `en` for the app. Check the installed size afterwards.
- [x] **L13** Show interpretation warnings under the explanation on both prompt paths.
- [x] Rebuild the installer, reinstall over 1.0.1 as 1.0.2, and re-run the Ollama, approval, trigger, and corrupt-state checks live.

## Status

Complete. Re-tested live on 1.0.2: Ollama now starts when it is installed but not running, answers in 10-14 s with `phi3`, and is stopped afterwards; a model choice that disagrees with the wording is not compiled; the corrupt state file was set aside once; uninstall still removes the program folder and the install log has no `RemoveFolderEx` error. The installed size went from 140 MB to 132 MB. The live `prolasso.ini` hash was unchanged at the end.
