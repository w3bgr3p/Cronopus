# Changelog

## 1.1.3.2

- Tasker: use segmented controls for schedule mode, frequency, start, repeat and finish options; distinguish enabling a schedule from running a task.
- Traffic: show the journal tab only for XML tasks and return to Execution when switching to an unsupported executor.
- Scheduler: clear stale Running status for disabled tasks during startup without restarting them.
- Remote Tasker: sort saved connections alphabetically in the node selector.
- Interface: align ZP7 styling with Tasker and use shared SVG actions throughout Config and Remote Tasker, including loading states and tooltips.
- Documentation: expand the external Tasker HTTP API reference with request fields, revision handling, payload operations and task-copy examples.

## 1.1.3.1

First release under the Cronopus name, following z3nDash 1.1.3.

- Branding: rename the application, executable, solution, assemblies and namespaces to Cronopus; replace the application icon and update page titles, setup, documentation and release announcements.
- Migration: import an existing z3nDash installation into a separate Cronopus directory on installation or first dashboard startup. Copy missing user files and saved Remote Tasker state, back up SQLite databases including committed WAL data, and relocate configured paths within the old installation. Keep the source installation intact and accept configuration property names regardless of case.
- Tasker overview: replace the summary table with interactive task maps for results and resources. Size blocks by failed/successful attempts, failure/success rate, current status, RAM or CPU; show zero and unavailable measurements separately. Retain focus and collapsed state during refreshes.
- Tasker and ZP7: share compact SVG action buttons with tooltips, accessible names, keyboard focus and dependency-installation progress. Group related actions and display the bulk-selection count separately.
- Tasker: simplify dependency actions to package editing and npm/pip installation, retain npm script selection, and reject stale asynchronous script responses after task changes. The old config.py/config.json toolbar shortcuts are removed.
- Resources: avoid the machine-wide process-tree query when no external task roots are running, while continuing to sample machine CPU and RAM.
- Process lifetime: protect newly launched external Tasker processes with a Windows Job Object so their trees terminate when the dashboard exits. Stop scheduling during shutdown and dispose the scheduler on tray exit.
- Maintenance: bundle a standalone orphan-process cleanup utility with preview by default, exact Python-script matching, an optional parent-PID filter and explicit --apply termination.
- Remote transfers: retain exception details, HRESULT and applicable native error codes for upload/transfer failures.
- Integrations: rename the Python SDK, injected environment variables, MCP discovery identity/resources and bridge-state override to Cronopus; update the associated harnesses and English/Russian documentation.
- Updates: use the renamed GitHub repository, Cronopus portable assets and executable for subsequent automatic updates.

### Upgrading from z3nDash

- Download and install Cronopus manually for this transition: the z3nDash 1.1.3 updater expects the old asset and executable names. Close the old dashboard before starting Cronopus to avoid running two schedulers. Keep the old installation until the imported setup has been reviewed.
- Install into a separate directory. Discovery checks registered z3nDash installations, the default LocalAppData directory and a sibling z3nDash directory; a portable installation elsewhere needs its data moved manually. Import skips an existing destination configuration or completed-import marker.
- Change Python imports from `z3ndash` to `cronopus`; change direct `Z3NDASH_*` script environment references to `CRONOPUS_*`. Update scripts that explicitly reference the `z3nDash` C# namespace or assembly.
- Point MCP clients at `Cronopus.exe --mcp`; resource URIs now start with `cronopus://`. Replace custom `Z3NDASH_BRIDGE_STATE_DIR` overrides with `CRONOPUS_BRIDGE_STATE_DIR`.
- On Remote Tasker servers, open/update the firewall rule for Cronopus: the new executable has its own rule identity. Previously exported connection codes use the old product marker; export a new code from Cronopus when adding a connection.

### Downloads

- `Cronopus_Setup_1.1.3.1.exe`: Windows x64 installer.
- `Cronopus_1.1.3.1_portable.zip`: self-contained Windows x64 build; no separate .NET installation required.
