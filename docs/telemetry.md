# Telemetry

Diagnostics sample every 2 seconds into a rolling window of the last 30 samples. The oldest sample is dropped, and sampling continues for as long as the Diagnostics page is open. It pauses when you leave the page or check the close approval box, and stops when you close the window. After a pause, the first sample has no CPU delta, so time away is not counted as CPU use.

CPU is a delta of processor time over the wall interval. It is shown as a fraction of logical processor capacity and as logical-core equivalents. A missing counter is unavailable, not zero. The first sample has no delta yet.

Working set is shown in bytes and is labeled as not memory pressure. GPU engine utilization is unavailable, not zero. The collector records its own duration.

Process rows include PID and creation time when the process allows it. Access denied and exited stay explicit. Command lines are not collected.

`--check` takes one inventory pass and prints `Sampling loop: not started`.

No telemetry leaves the machine.
