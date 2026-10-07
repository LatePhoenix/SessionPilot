# SessionPilot remediation plan

Source: code audit of `main` at `3628bb3`, 2026-10-07. Baseline: `dotnet test SessionPilot.slnx` passed 86 tests.

This plan covers every finding from that audit. It is written for an agent working phase by phase. Read **How to work** once, then read only the phase you are on.

---

## How to work

### Ground rules

- `.cursor/rules/sessionpilot-guardrails.mdc` still applies. Nothing in this plan enables live Process Lasso writes, power-plan switching, VR settings writes, force termination of user applications, or an elevated helper. If a fix seems to need one of those, stop and record it under **Blockers**.
- Stopping an `ollama serve` process that SessionPilot itself started is allowed. It is the existing behavior.
- The repository is **public**. Never commit secrets, a real `prolasso.ini` or its values, a Windows profile path with a user name, or machine-specific output. Test fixtures use synthetic data.
- Match the surrounding code: file-scoped namespaces, `sealed record` DTOs, plain status strings such as `not-measured`, and short declarative user-facing sentences that say what did *not* happen ("Nothing was written.").
- `TreatWarningsAsErrors` is on. A build warning is a failure.

### Testing

- Run `dotnet test SessionPilot.slnx --nologo` before every push. All tests must pass, and the count must not drop below the previous phase's count.
- Every bug fix gets a regression test that fails before the fix and passes after it. Write the test first when that is practical.
- The test project targets `net10.0` and cannot reference the WPF app. When logic in `MainWindow.xaml.cs` needs a test, move it into `SessionPilot.Core` or `SessionPilot.Infrastructure` and keep the window as thin wiring. Several items below say exactly what to move.
- Do not write tests that start real `ollama`, `powercfg`, or GUI processes, or that touch `ProgramData\ProcessLasso`. Use the existing seams (`IFileReplacer`, `IOllamaHost`, `IProcessStarter`, `ICloseRequest`, fake `HttpMessageHandler`), or add a narrow one.
- After a phase that changes the WPF app (Phases 1, 4, 6, 7), build it with `dotnet build src/SessionPilot.App/SessionPilot.App.csproj`. If you can, launch it once to confirm it opens. Record anything you could not verify in the PR body.

### Repository management (autonomous, keep it cheap)

The owner does not want to be asked about routine repo operations. Do them yourself, at minimum cost.

- **One branch and one PR per phase.** Branch names are `fix/phase-N-short-name`, created from an up-to-date `main`. Do not open a PR per item.
- **Commit messages** follow the repository's style: one sentence in plain English ending with a period, for example `Keep a corrupt journal from stopping startup.`. Use a few logical commits per phase, not one per file.
- **Push once** when the phase is complete and green locally. Avoid push-fix-push loops: CI is about one minute on `windows-latest` and runs on every push.
- **Open the PR** with `gh pr create --fill` or a short body: what changed, the test count before and after, the checklist IDs closed, and anything not verified. No screenshots, labels, reviewers, milestones, or projects.
- **Wait for CI once** with `gh pr checks <n> --watch --fail-fast`. This is a single blocking call. Do not poll in a loop or schedule re-checks.
- **Merge** with `gh pr merge <n> --squash --delete-branch` when checks pass, then `git switch main && git pull --ff-only`.
- **If CI fails:** reproduce locally, fix, and push once. Retry a run that looks flaky only once (`gh run rerun <id> --failed`). After two failures on the same cause, stop, write the cause under **Blockers**, and leave the PR open.
- **Do not** create issues, releases, tags, GitHub Projects, wiki pages, branch protection, rulesets, or repository settings changes. Never force-push `main` or rewrite its history.
- **Keep the tracker current.** Tick checklist items in this file in the same PR that fixes them. That is the only progress record. Do not create separate status files.

### Token budget

- Read this file's **How to work** section and the current phase only. Read source files you are changing. Use targeted search (`rg`) instead of reading whole directories.
- Do not re-audit the codebase. Findings are already located below. If you find a new issue, add it under **New findings** with a one-line description and continue. Fix it only if it is small and in a file you are already changing.

---

## Phase 0: Handoff and CI

Branch: `chore/remediation-handoff`. It already exists locally with this plan and `.cursor/rules/remediation-workflow.mdc` committed. It has not been pushed.

- [x] **0.1 Push the handoff branch together with the CI changes below, open the PR, and merge it.**
- [x] **0.2 Harden `.github/workflows/test.yml`.**
  - Trigger on `push` to `main` and on `pull_request` targeting `main`.
  - Add `paths-ignore: ['docs/**', '**/*.md', '.cursor/**']` to both triggers so documentation-only changes do not spend a run. Note: the Phase 0 PR changes the workflow file itself, so it still runs.
  - Add top-level `permissions: contents: read`.
  - Add `concurrency: { group: test-${{ github.ref }}, cancel-in-progress: true }`.
  - Pin `actions/checkout` and `actions/setup-dotnet` to full commit SHAs with a trailing `# v4` comment. Resolve each SHA with one `gh api repos/actions/<name>/commits/v4 --jq .sha` call.
  - Keep a single job. Do not add the WiX installer build to CI.
- [x] **0.3** Update the last line of `docs/testing.md` to describe the new triggers.

Acceptance: the PR's CI run is green and `main` contains the plan.

---

## Phase 1: Startup and crash resilience

Branch: `fix/phase-1-resilience`. Highest priority. A single bad file currently stops the app at startup.

- [x] **1.1 Add a global exception handler for the WPF app.** In `src/SessionPilot.App/App.xaml.cs`:
  - Handle `DispatcherUnhandledException`. Show a `MessageBox` with `CheckReport.Redact(exception.Message)` and a sentence that nothing was written, then set `e.Handled = true`.
  - Also subscribe to `TaskScheduler.UnobservedTaskException` (call `SetObserved`) and `AppDomain.CurrentDomain.UnhandledException` (best-effort message).
  - Do not write a log file in this phase.
- [x] **1.2 Let `--check` fail cleanly.** In `App.OnStartup`, wrap the `--check` path in try/catch. On failure, write a redacted one-line error and call `Shutdown(1)`. Success stays `Shutdown(0)`.
- [x] **1.3 Skip unreadable journals instead of throwing.** `JournalRecovery.FindIncomplete` (`src/SessionPilot.Core/Transactions/Transactions.cs:257`) throws on a corrupt, empty, or locked journal.
  - Catch `JsonException`, `IOException`, and `UnauthorizedAccessException` per file.
  - Return the unreadable files separately. A suggested shape: `JournalScan { IReadOnlyList<JournalRecord> Incomplete; IReadOnlyList<string> UnreadableFileNames }`. Keep `FindIncomplete` as a thin wrapper if existing tests use it.
  - `StartupRecovery.DescribeIncomplete` (`ApprovalBinding.cs:28`) adds a line per unreadable journal, file name only, ending "It was not opened or rolled back."
  - `JournalRecovery.Describe` must also tolerate an unreadable target file and return "The target could not be read. No rollback was applied."
  - Tests: a corrupt JSON file, an empty file, a valid incomplete journal beside a corrupt one, and a locked target in `Describe`.
- [x] **1.4 Skip a bad user loadout instead of failing the whole catalog.** `LoadoutCatalog.Load` (`src/SessionPilot.Core/Planning/LoadoutCatalog.cs:19`) throws on one bad user file.
  - Built-in presets keep throwing: a broken built-in is a packaging bug.
  - A user-directory file that fails JSON parsing or validation, or that collides with an existing id, is skipped and recorded in a new `IReadOnlyList<string> LoadErrors` on the catalog. Record the file name and reason only.
  - `MainWindow.LoadShell` shows `LoadErrors` on the Dashboard.
  - Tests: an invalid JSON user file, a user file with `priorityPolicy: "high"`, and a collision with `balanced`. In each case the catalog still loads and the error is listed.
- [x] **1.5 Guard file reads in the window.** In `MainWindow.ApproveCurrentPlan` and `ApplyIsolated` (`MainWindow.xaml.cs:478`, `:506`), wrap hashing in try/catch for `IOException` and `UnauthorizedAccessException`. Uncheck approval and show "The isolated copy could not be read. Nothing was written."
- [x] **1.6 Stop path normalization from throwing.** `TransactionCoordinator.IsLivePath` (`Transactions.cs:161`) calls `Path.GetFullPath`, which can throw on malformed input outside the try block.
  - Normalize inside a helper that returns `false` for a path that cannot be normalized, on the candidate side only.
  - If the *target* cannot be normalized, refuse with "The target path is not valid."
  - Move `Directory.CreateDirectory(request.JournalDirectory)` after the approval and live-path refusals so a refused request creates nothing.
  - Tests: a target with an invalid path character is refused, and a refused unapproved request does not create the journal directory.

---

## Phase 2: Correctness and privacy in Core

Branch: `fix/phase-2-core-correctness`.

- [x] **2.1 Make path redaction cover every path.** `CheckReport.Redact` (`src/SessionPilot.Core/Diagnostics/CheckReport.cs:53`) stops at the first space. The audit reproduced `C:\Program Files\Process Lasso\prolasso.ini` → `[path] Files\Process Lasso\prolasso.ini`, and `C:/Users/alice/x.ini` was not redacted.
  - Redact from a drive prefix `(?<![A-Za-z0-9])[A-Za-z]:[\\/]` through the end of the path. Treat everything up to a line break, a quote, `<`, `>`, or `|` as path. Over-redacting trailing words is acceptable; leaking path segments is not.
  - Do the same for UNC paths (`\\server\share...` and `//server/share...`).
  - Also replace the current `Environment.UserName` when it is at least 3 characters, case-insensitive, with `[user]`. Add an overload `Redact(string text, IEnumerable<string> extraTokens)` so tests do not depend on the machine.
  - Tests: spaces in the path, forward slashes, UNC, a path followed by a sentence, a URL `http://example.test/a` that must *not* be redacted, a time `12:30` that must not be redacted, and a supplied user-name token.
  - Keep the existing `CheckReport_RedactsPaths_AndDoesNotSample` test passing.
- [x] **2.2 Make trigger dwell work regardless of line order.** The window resets the pending timer from the first signal line while the engine picks by precedence. The audit reproduced signals `balanced` + `desktop-gaming` returning `wait` forever.
  - Move pending-state tracking into Core: a `TriggerTracker` class that holds `PendingLoadoutId` and `PendingSince`, and an `Evaluate(signals, manualLoadoutId, optedIn, now)` method that calls `TriggerSuggestions.Suggest` and updates the state from the decision. Only the engine decides when to reset.
  - `MainWindow.SuggestTrigger` (`MainWindow.xaml.cs:371`) uses the tracker and removes its own `_triggerPendingId` and `_triggerPendingSince` logic.
  - Tests: a lower-precedence first line still reaches `suggest` after `MinimumDwell`. Changing the top signal restarts the dwell. Opt-out still holds.
- [x] **2.3 Match whole words in the deterministic interpreter.** `DeterministicInterpreter` (`src/SessionPilot.Core/Planning/Interpretation.cs:17`) has false positives. Reproduced: "adjust display settings" → `desktop-gaming`, and "run diagnostics while coding" → `vrchat-diagnostic`.
  - Replace `Contains` with whole-word or whole-phrase matching: precompiled `Regex` with `\b` boundaries, `RegexOptions.CultureInvariant`. Remove the trailing-space hacks (`"play "`, `"build "`, `"kill "`) and match `\bplay\b`, `\bbuild\b`, `\bkill\b`.
  - `crowded` or `diagnostic` route to `vrchat-diagnostic` only when `vrchat` is also present. Otherwise they fall through to the general matchers.
  - Leave `virtual desktop` → `vrchat-social` as it is.
  - Apply the same path-like-name filter that `IntentValidator` uses (`\ / : * ? "` and `..`) to quoted names in `RequestedApplications`.
  - Tests: the two reproduced phrases, "display" alone, "rebuild the solution" still matching `development-build-heavy` (add `rebuild` as its own token), and that all existing interpreter `InlineData` cases still pass. Do not change them.
- [x] **2.4 Fix temp-file cleanup when a replace fails.** `SameVolumeFileReplacer.Replace` (`Transactions.cs:12`) copies `ReadOnly` onto the temp file, so the cleanup `File.Delete` throws and hides the real error. Reproduced: `UnauthorizedAccessException` naming the `.tmp` file, with the temp file left behind.
  - If the target has `ReadOnly`, throw `IOException("The target is read-only.")` before writing a temp file.
  - Copy attributes with `ReadOnly` masked out.
  - In `finally`, reset the temp file's attributes to `Normal`, then delete it inside its own try/catch, so cleanup never replaces the original exception.
  - Tests: with a read-only target, the coordinator returns `failed` with a read-only message and no `.sessionpilot-*.tmp` remains. Restore attributes in test cleanup.
- [x] **2.5 Check `schemaVersion` without throwing.** In `IntentValidator.ValidateJson` (`Interpretation.cs:247`), use `version.TryGetInt32(out var v) && v == Schema.Current`. Test: `1.5`, `1e30`, and `"1"` each return a failed interpretation and do not throw.

---

## Phase 3: Ollama lifecycle

Branch: `fix/phase-3-ollama`.

- [x] **3.1 Accept `::1` as a loopback endpoint.** `OllamaIntentClient.IsLoopback` (`src/SessionPilot.Core/Ollama/OllamaIntentClient.cs:142`) never matches it, because `Uri.Host` is `[::1]`. Use `uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback`. Tests: `http://[::1]:11434` and `http://localhost:11434` are accepted. `https://127.0.0.1`, `http://192.168.1.2`, and `http://127.0.0.1.example.test` are refused.
- [x] **3.2 Treat a non-success reply on the Ollama port as "something else is listening".** In `ProbingOllamaHost.ProbeAsync` (`OllamaHost.cs:62`), a 404 from `/api/version` currently means "down" and leads to running `ollama serve`.
  - Any HTTP response means something is listening. A non-success status returns `Unusable` with "Something is listening on the Ollama port but did not answer a version check. Nothing was started."
  - Only `HttpRequestException` (connection refused) means `Down`.
  - Tests use a fake handler and a fake `IOllamaLauncher` that counts calls: 404 does not launch, connection refused does launch, and 200 does not launch.
- [x] **3.3 Stop the server SessionPilot started when the readiness wait fails.** `OllamaServeLauncher.StartAsync` (`OllamaHost.cs:100`): if `WaitAsync` throws (a 2-second `HttpClient` timeout surfaces as `TaskCanceledException`, or the user cancels), the started `ollama serve` is never stopped.
  - Wrap the wait in try/catch: on any exception, `OllamaSession.Stop(process)` and rethrow.
  - Inside `WaitAsync`, treat `TaskCanceledException` when the caller's token is not cancelled as "not ready yet" and keep polling.
  - To test it, add a seam. Extract the readiness loop to `internal static Task<bool> WaitForReadyAsync(Func<CancellationToken, Task<bool>> probe, Func<bool> hasExited, TimeSpan budget, CancellationToken ct)`, add `InternalsVisibleTo("SessionPilot.Tests")` to Core, and test that a probe throwing `TaskCanceledException` keeps polling and that caller cancellation propagates. Test the try/catch-stop path through a small `IStartedProcess` abstraction if that stays simple; otherwise note it in the PR as reviewed by reading.
- [x] **3.4 Ignore relative PATH entries when locating `ollama.exe`.** In `OllamaServeLauncher.FindExecutable` (`OllamaHost.cs:153`), skip entries that are not fully qualified (`Path.IsPathFullyQualified`) after trimming whitespace and surrounding quotes. Test with an injected PATH string: move the PATH parsing into a pure helper that takes the string.
- [x] **3.5 Re-check the session gate after the request.** In `MainWindow.InterpretWithOllama` (`MainWindow.xaml.cs:322`):
  - After the `await`, check `OllamaSessionGate.Allow(_session.Phase)` again. If the session became Active, show the result text but do not call `Compile`, and say "A session became Active. The plan was not recompiled."
  - Disable the Ollama button while a request is running, to stop concurrent clicks.
  - Cancel any pending request when the window closes: keep a `CancellationTokenSource` field and cancel it in `Closed`.
- [x] **3.6 Give cold model loads a realistic time budget.** `keep_alive` 0 means every request is a cold load, and the single 30-second budget covers both server startup (up to 20 seconds) and inference.
  - Split it into `StartupBudget` (default 20 seconds) and `RequestTimeout` (default 60 seconds) on `OllamaRequest`.
  - Update `InterpretAsync` so starting the server and the chat call each use their own budget.
  - Test that both defaults are applied and that a timed-out chat returns "timed out" and still disposes the session.

---

## Phase 4: Infrastructure processes

Branch: `fix/phase-4-infrastructure`.

- [x] **4.1 Give the `powercfg` read a real timeout.** `PowerPlanReader.TryList` (`src/SessionPilot.Infrastructure/Discovery/PowerPlanReader.cs:7`) calls `ReadToEnd()` before `WaitForExit(4000)`, so the timeout never applies, and stderr is redirected but never read.
  - Use the full path `Path.Combine(Environment.SystemDirectory, "powercfg.exe")`.
  - Do not redirect stderr.
  - Read stdout with `ReadToEndAsync`, combined with `WaitForExitAsync` under a 4-second `CancellationTokenSource`. On timeout, `Kill()` the `powercfg` process this code started (it is SessionPilot's own child, not a user application) and return `null`.
  - Make it `TryListAsync`, and have `MainWindow` await it (see 6.2).
- [x] **4.2 Parse power plans on any Windows display language.** `PowerPlanParser.Parse` (`src/SessionPilot.Core/Power/PowerOwnership.cs:26`) only finds the English text `Power Scheme GUID:`.
  - Find a GUID with a regex (`[0-9a-fA-F]{8}-(?:[0-9a-fA-F]{4}-){3}[0-9a-fA-F]{12}`) on each line, take the name from the last `( … )` after it, and take `Active` from a trailing `*`.
  - Tests: the existing English sample, a synthetic German-style line (`GUID des Energieschemas: 381b4222-f694-41f0-9685-ff5bb260df2e  (Ausbalanciert) *`), and lines without a GUID being ignored.
- [x] **4.3 Pass launch arguments with standard Windows escaping.** `ShellStarter.Quote` (`MainWindow.xaml.cs:602`) strips embedded quotes and breaks on trailing backslashes and tabs.
  - Move `ShellStarter` into `SessionPilot.Infrastructure` as a public `ShellProcessStarter : IProcessStarter`.
  - Build the start info in a pure static method `CreateStartInfo(pathOrUri, arguments)` that fills `ProcessStartInfo.ArgumentList`. Keep `UseShellExecute = true`, which URIs need. .NET applies standard Windows argument escaping to `ArgumentList` either way.
  - Tests on `CreateStartInfo`: an argument with spaces, an embedded quote, a trailing backslash, and an empty argument each round-trip through `ArgumentList` unchanged. Do not start a process in tests.
- [x] **4.4 Move `LiveWindowCloser` as well.** Move it to Infrastructure next to `ShellProcessStarter`, so the window holds no process logic. No behavior change.

---

## Phase 5: INI and transaction hardening

Branch: `fix/phase-5-ini-transactions`. Nothing writes to a live file today, but these must be right before any codec is ever enabled.

- [x] **5.1 Keep the spacing after `=` when editing.** `IniDocument.Apply` (`src/SessionPilot.Core/Ini/IniDocument.cs:130`) rewrites `Key = old` as `Key =new`.
  - Keep the original value's leading whitespace, and trailing whitespace if present.
  - The `Value` stored on the line stays the raw text after `=`, so `RestorePlanner` comparisons keep working. Update `CaptureOwned` so `WrittenValue` and `BaselineValue` are compared consistently: either both raw or both trimmed. Document the choice in a code comment.
  - Tests: spacing is preserved, a byte-for-byte round trip with no edits still holds, and the restore analysis still finds a restorable value after an edit with spacing.
- [x] **5.2 Refuse values the file's encoding cannot store.** Reproduced: writing `日本` into a Latin-1 file produced `??`.
  - In `Apply`, check each value with a strict encoder (`Encoding.GetEncoding(name, EncoderFallback.ExceptionFallback, …)`, or by round-tripping) for the document's encoding.
  - Reject with "Value for [S] K cannot be represented in the file's Latin1 encoding."
  - Tests: Latin-1 with CJK is rejected, Latin-1 with `é` is accepted, and UTF-8 with CJK is accepted.
- [x] **5.3 Refuse duplicate edits.** In `Apply`, two edits for the same section and key (case-insensitive) are rejected with "[S] K is edited more than once." Test it.
- [x] **5.4 Refuse new values that would read as an inline comment.** In `Apply`, reject a *new* value that contains ` ;` or ` #`, using the same rule as `HasInlineComment`. Test it.
- [x] **5.5 Bind the full plan in the approval hash.** `ApprovalBinding.HashPlan` (`src/SessionPilot.Core/Transactions/ApprovalBinding.cs:7`):
  - Include `TargetIdentity`, `Section`, `Key`, `ExistingValue`, `ProposedValue`, `Writable`, and `SupportStatus` for each change, plus `LoadoutId`, `Summary`, and the intent fields.
  - Use unambiguous framing: length-prefix each field (`{len}:{value}`) instead of `|` and `\n` separators.
  - Do not include `PlanId`. It is a fresh GUID on every compile, and including it would make re-compiling the same plan look different, a separate design decision.
  - Tests: changing `Section` changes the hash, two plans whose fields differ only in where a `|` falls hash differently, and recompiling the same loadout gives the same hash.

---

## Phase 6: UI responsiveness and quality of life

Branch: `fix/phase-6-ui-qol`.

- [ ] **6.1 Take diagnostics samples off the UI thread.** `MainWindow.SampleTick` (`MainWindow.xaml.cs:122`) enumerates every process on the UI thread every 2 seconds.
  - Move collection into Infrastructure: `ProcessSampler.Sample(previousCpu, wall, logicalProcessors)` returns rows plus the updated CPU map, and the window runs it with `Task.Run`.
  - Skip a tick if the previous sample is still running.
  - Prune the previous-CPU map to the keys seen in the latest sample. That also fixes the never-pruned cache.
  - Keep the row semantics exactly as they are: `access-denied`, `exited`, and `unavailable` strings.
  - Unit-test the pruning and the CPU delta on synthetic input, not live processes.
- [ ] **6.2 Run startup discovery asynchronously.** `MainWindow.LoadShell` runs topology, `powercfg`, journal scan, and catalog load synchronously in `Loaded`.
  - Make it `async`. Run blocking discovery on `Task.Run`. Show "Discovering…" placeholders.
  - Keep navigation usable while discovery runs, and guard handlers that need `_catalog` (they already return early when it is null).
- [ ] **6.3 Let the user clear a manual loadout selection.** Once a loadout is picked by hand, triggers stay overridden until restart.
  - Add `SessionCoordinator.ClearManualSelection()` and a "Clear manual selection" button on the Loadouts page that also clears the list selection.
  - Test the coordinator method.
- [ ] **6.4 Let the user change or reset the power owner.** Today the recorded owner cannot be changed.
  - Add a "Clear recorded owner" action that returns `PowerOwnerKind.Unset` with "The recorded owner was cleared. Nothing was switched."
  - Add `PowerOwnership.Clear()` in Core and test it. The refusal of a *second* concurrent owner stays.
- [ ] **6.5 Explain the next step when Continue cannot proceed.** `ContinueSession` (`MainWindow.xaml.cs:430`) always says "Compile a plan before preparing."
  - Move the next-phase mapping into Core, `SessionCoordinator.NextPhase(bool planCompiled)`, returning the next phase or a reason. Reasons: Idle → "Press Begin to start a session."; AwaitingApproval without a plan → "Compile a plan before preparing."; terminal phases → "Press Begin to start again."
  - Test the mapping.
  - Also remove the redundant `Compiled = _status.Compiled` in the `with` expression at `:452`.
- [ ] **6.6 Keep the power owner and measurement notes across restarts.** Both are memory-only today.
  - Store them in `%LOCALAPPDATA%\SessionPilot\state.json` through a small `AppStateStore` in Infrastructure, using the same atomic temp-then-replace pattern as `SameVolumeFileReplacer`.
  - Corrupt state is ignored and reported once, matching Phase 1's tolerance rules.
  - Measurement rows keep `PerformanceEffect = not-measured`.
  - Update `docs/privacy.md`: measurement notes now persist on this machine until deleted. Add a "Clear measurement notes" button.
  - Tests: round-trip, corrupt file, and a missing directory.

---

## Phase 7: Packaging and app metadata

Branch: `fix/phase-7-packaging`. Build the installer locally once (`dotnet tool restore` then `dotnet build installer/SessionPilot.Installer/SessionPilot.Installer.wixproj -c Release`). CI does not build it.

- [ ] **7.1 Allow clean upgrades.** Add `<MajorUpgrade DowngradeErrorMessage="A newer version of SessionPilot is already installed." />` to `installer/SessionPilot.Installer/Package.wxs`. Without it, a higher version installs side by side.
- [ ] **7.2 Keep the version in one place.**
  - Add `<Version>1.0.0</Version>` to `Directory.Build.props`.
  - In `Package.wxs`, set `Version="!(bind.FileVersion.<file id of SessionPilot.App.exe>)"`. If the harvested `Files` element does not give a stable id, pass `$(Version)` through `DefineConstants` in the `.wixproj` and use `Version="$(var.Version)"`.
  - Confirm the MSI's ProductVersion matches the executable's.
- [ ] **7.3 Remove leftover folders on uninstall.** ICE64 is suppressed, so empty harvested subfolders such as `presets\` stay behind.
  - Add `WixToolset.Util.wixext` 6.0.2 and use `util:RemoveFolderEx On="uninstall" Property="SESSIONPILOT_INSTALLFOLDER"`, with the property read back from the existing `HKCU\Software\SessionPilot` value through a `RegistrySearch`. Store the folder path there as a string value; the existing integer key path can stay.
  - Verify by installing and then uninstalling once, and checking that `%LOCALAPPDATA%\Programs\SessionPilot` is gone.
  - `%LOCALAPPDATA%\SessionPilot` (user data and journals) must *not* be removed. Say so in the README.
- [ ] **7.4 Add an icon and an application manifest.**
  - Add `src/SessionPilot.App/app.ico`, a simple original icon generated in-repo with no third-party art, and set `<ApplicationIcon>`. Use it for the shortcut (`Icon` element) and set `ARPPRODUCTICON`.
  - Add `app.manifest` with per-monitor V2 DPI awareness, `longPathAware`, and `asInvoker`, and reference it from the csproj.
- [ ] **7.5 Correct the executable name in the docs.** `docs/manual-verification.md` says `SessionPilot --check`, but the executable is `SessionPilot.App.exe`. Change the docs. Do not rename the assembly; that would change the installer and shortcut. Mention the `--check` exit codes (0 or 1, from Phase 1).
- [ ] **7.6 Note console behavior for `--check`.** It is a `WinExe`, so its output can interleave with the shell prompt. Document `start /wait SessionPilot.App.exe --check` for cmd, and `& .\SessionPilot.App.exe --check | Out-String` for PowerShell, in the README and in `docs/manual-verification.md`. No code change.

---

## Phase 8: Cleanups and documentation

Branch: `fix/phase-8-cleanup`. Small and quick. Combine into one PR.

- [ ] **8.1** Remove the no-op conditional `access == "ok" ? row.CreationTime : row.CreationTime` in `src/SessionPilot.Core/Discovery/InstallationCandidates.cs:102`.
- [ ] **8.2** Remove the always-true `System.Text.Encoding.UTF8.GetCharCount(bytes) >= 0 &&` in `IniDocument.Decode` (`IniDocument.cs:297`).
- [ ] **8.3** `TriggerSuggestions.Suggest` has a guard that cannot be reached. Either fold it into `TriggerEngine` with a comment explaining the "suggest-only" contract, or keep it and add a test that pins the contract (the engine never returns `apply`). Prefer the test, because the wrapper is the documented boundary.
- [ ] **8.4 Document discovery limits.** Discovery only probes the default Process Lasso install and config locations (`LiveDiscovery.cs:9`). The guardrails forbid inventing registry locations, so do not add one. Record it as a known limitation in `docs/compatibility.md`. Optionally, add a user-entered candidate path that is probed read-only exactly like the default. Only do that if it stays small.
- [ ] **8.5 Update the docs.**
  - `docs/testing.md`: the new test areas.
  - `docs/roadmap.md`: what shipped.
  - `docs/privacy.md`: redaction scope, persisted state.
  - `README.md`: the `--check` exit code, upgrade behavior, and that uninstall keeps user data.
  - Add a dated line to `docs/refactor-audit.md` pointing to this plan.
- [ ] **8.6** Tick any remaining boxes, then move this file's status line (below) to "Complete".

---

## Finding → item index

| Finding | Item |
|---|---|
| Startup stopped by a corrupt journal | 1.1, 1.3 |
| Startup stopped by a bad user loadout | 1.1, 1.4 |
| Window stopped by a locked isolated file | 1.5 |
| Path redaction leaks spaces and forward-slash paths | 2.1 |
| Trigger dwell never completes | 2.2 |
| Interpreter false positives | 2.3 |
| Replacer temp leak and masked error | 2.4 |
| `schemaVersion` `FormatException` | 2.5 |
| `::1` refused | 3.1 |
| 404 on the Ollama port starts a second server | 3.2 |
| Orphaned `ollama serve` | 3.3 |
| `powercfg` timeout ineffective, stderr redirected | 4.1 |
| Relative PATH lookup for `ollama.exe` | 3.4 |
| `powercfg` found through the search path | 4.1 |
| Argument quoting | 4.3 |
| INI spacing, encoding, duplicate edits, new inline comment | 5.1–5.4 |
| Approval hash scope and framing | 5.5 |
| Gate re-check after the Ollama request | 3.5 |
| `GetFullPath` outside the try block | 1.6 |
| No MajorUpgrade, leftover folders on uninstall | 7.1, 7.3 |
| CI triggers, permissions, pinning | 0.2 |
| Sampling on the UI thread, synchronous startup | 6.1, 6.2 |
| Manual override cannot be cleared, power owner sticky | 6.3, 6.4 |
| Misleading Continue message | 6.5 |
| Measurement log and power owner not persisted | 6.6 |
| No icon, version, or manifest | 7.2, 7.4 |
| `--check` exit code and console interleaving | 1.2, 7.6 |
| Wrong executable name in docs | 7.5 |
| Default-path-only discovery | 8.4 |
| English-only power-plan parsing | 4.2 |
| No-op conditional, tautology, dead guard, unpruned CPU cache | 8.1–8.3, 6.1 |
| Cold-load timeout budget | 3.6 |

## Status

Not started.

## New findings

(none yet)

## Blockers

(none yet)
