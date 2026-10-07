# Transactions

Configuration edits use the isolated transaction coordinator:

1. Approval is required.
2. A live candidate path is refused while `LiveApplyPolicy.Enabled` is false.
3. An empty edit list does not write. Current compiled plans have no writable edits.
4. When an isolated copy does have edits, the coordinator records a baseline hash, writes a backup, rechecks the hash, replaces the file, and compares the result.
5. Governor consumption and effective settings are not claimed for an isolated file. Performance effect stays `not-measured`.

Startup calls `StartupRecovery.DescribeIncomplete` on `%LOCALAPPDATA%\SessionPilot\journals`. It explains an incomplete journal and does not roll it back.

Approval stores the plan hash and the target file hash. Recompiling the plan or changing the file invalidates it.

Three-way restore compares baseline, app-written, and current values. External edits become conflicts. Whole-file restore is not automatic.

There is no elevated helper.
