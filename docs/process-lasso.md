# Process Lasso integration

Process Lasso is the enforcement product. SessionPilot does not replace its scheduler and does not ship its binaries.

What works today is read-only discovery plus a dry-run plan:

- Candidate executable and `prolasso.ini` locations, with confidence left low until a person confirms the active file.
- Lossless INI parsing for isolated copies, including unknown sections and ambiguous keys.
- Capability notes for performance mode, Efficiency Mode, CPU priority, ProBalance exclusions, CPU Sets, watchdogs, and affinity. Empty observed keys are not a populated fixture.
- Built-in loadouts keep priority and CPU placement `unchanged` and ProBalance `preserve`.

What stays disabled:

- Any write to a live candidate path.
- Serialization of rule lists. Historical examples disagree, so `HistoricalRuleParsers.SerializationAllowed` is false.
- Real-time and High priority.
- Governor restart, reload claims, and a JSON import endpoint. A manual preview is not an export from Process Lasso.
- An elevated helper. The candidate INI is not treated as user-writable, and there is still no verified codec to send across that boundary.

The missing evidence is a version-tagged before/after Export Rules JSON and INI pair, plus a reload check that does not restart the governor to hide uncertainty.
