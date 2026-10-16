# System Snapshot

System Snapshot collects a diagnostic snapshot of the current Windows system.

## Refresh

Opening the page starts a capture automatically on the machine z3nDash runs on. `Refresh` collects current data again. The page includes the sections the backend actually managed to gather: processes, network and system data.

The page data lives in memory only.

## Memory allocation

The summary shows physical RAM and Windows commit. Commit includes the current
allocation, limit, remaining headroom and peak since boot; paged and nonpaged
kernel pools are shown alongside it. Commit usage warns at 90% and becomes
critical at 97%, even when physical RAM is still available.

Process groups show private allocation and resident RAM separately, summed across
instances. Unreadable private-memory measurements are counted so incomplete totals
remain visible.

An interactive map sizes blocks by each process group's memory use.
`Block size` switches between private allocation and resident RAM. Shares use
the displayed process total; filters apply to both the map and the table.
Resident totals can count shared pages more than once, and process private
allocation does not cover all system commit.

Select a block to list its PIDs, then choose a process to open its command line
and actions in the sidebar. `Refresh` collects new measurements.

## CPU

The `CPU usage` section shows current workload by process group as a map
and a table with `CPU_PCT`. Usage is sampled over a short interval;
100% means all logical CPUs are busy. `CPU_SEC` is lifetime processor time,
not current load.

Block area shows the group's share of displayed workload, excluding idle CPU.
Selecting a block lists its PIDs sorted by current CPU. Unreadable measurements
are marked unavailable. Process totals can differ from system load because of
kernel work, different sampling intervals and processes that exited.

## Environment

The first item in the left navigation, not part of the snapshot. It lists the tools
this machine has for running tasks, showing what the command, registry or connection
actually returned. A check takes a few seconds, so it runs on its own only the first
time the section opens; after that, use `Check`. A missing tool has an `Install`
button: the installer opens in a separate console window; press `Check` when it finishes.

An open Environment section survives `Refresh` and the next visit to the page.

The winget installer downloads App Installer and its dependencies from the same
release. If winget cannot start, it also downloads that release's license and
requests Windows UAC approval to repair provisioning. Success is reported only
after `winget --version` succeeds; a failed repair stays an installation error.

## Diagnostic recording

On instrumented dashboard pages, `Ctrl+Shift+D` starts or stops recording.
The recording badge also offers **Report** and **Stop**. Correlated browser and
server events help separate browser waiting, server queueing and handler time,
including database, scheduler-lock and console timings.

Recordings are saved as JSONL in the configured logs folder's `diag` directory.
Stopping writes a readable `.txt` report beside the dump. Reports include slow
requests, route timings and browser stalls; SQL events keep operation and table
names instead of query values.

## API

- `POST /system-snapshot/capture`
- `GET /config/environment`
- `POST /config/environment/install`
