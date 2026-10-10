# Performance baseline

Honest before/after numbers for SessionPilot 1.1. Later phases compare
against these rows. The owner fills in measured numbers from the command
below. Empty cells are not estimates.

## Command

Leave the named surface open for the whole run. Dashboard, Diagnostics,
and minimized are three separate runs of the same command. The script
prints to the console and writes nothing to disk.

```powershell
pwsh -NoProfile -File tools/measure-overhead.ps1 -ProcessName SessionPilot.App -Minutes 10 -IntervalSeconds 5
```

Copy these printed values into the table:

- CPU % of one core (mean): `CPU use, percent of one logical core over the run`
- CPU % of one core (max): the per-interval max
- Private bytes (min) and (max): `PrivateMemorySize64 (bytes)`
- Working set (mean): `WorkingSet64 (bytes)` mean
- Handles (max): `HandleCount` max
- Threads (max): `Threads` max

## Machine, build, and date

- CPU model:
- Build:
- Date:

CPU model only. No user names, account names, or machine names.

## Results

| Run | CPU % of one core (mean) | CPU % of one core (max) | Private bytes (min) | Private bytes (max) | Working set (mean) | Handles (max) | Threads (max) |
| --- | --- | --- | --- | --- | --- | --- | --- |
| 1.0.2 Dashboard 10 min |  |  |  |  |  |  |  |
| 1.0.2 Diagnostics 10 min |  |  |  |  |  |  |  |
| 1.0.2 minimized 10 min |  |  |  |  |  |  |  |
