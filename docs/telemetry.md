# Telemetry

Diagnostics use a rolling window of 30 samples at a 2 second interval. Sampling runs only while the Diagnostics page is open and stops when you leave the page or close the window.

CPU is a delta of processor time over the wall interval. It is shown as a fraction of logical processor capacity and as logical-core equivalents. A missing counter is unavailable, not zero. The first sample has no delta yet.

Working set is shown in bytes and is labeled as not memory pressure. GPU engine utilization is unavailable, not zero. The collector records its own duration.

Process rows include PID and creation time when the process allows it. Access denied and exited stay explicit. Command lines are not collected.

`--check` takes one inventory pass and prints `Sampling loop: not started`.

No telemetry leaves the machine.
