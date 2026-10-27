# Remote Tasker bridge verification

Date: 2026-10-15. Branch: `codex/tasker-remote-bridge`. Base: `adf25e0`.

## Delivered behavior

- Shared Tasker service used by the original local handler and a separate Kestrel HTTPS host.
- Persistent node identity, IP SAN certificate pinning, named/revocable keys and DPAPI state.
- Local backend proxy keeps connection secrets out of the browser.
- Task creation, cloning, settings/payload edits, schedule/control operations and revision conflicts.
- Bounded task/queue/instance pages, changes cursor and durable command results.
- Server disk/folder picker, directory creation and resumable native file/folder transfers.
- Block offsets, SHA-256, explicit overwrite decisions, per-file atomic publication.
- Explicit output load/observation with bounded server/browser buffers and gap markers.
- Russian/English setup documentation, certificate renewal and configurable transfer limits.

## Executed evidence

- .NET 10 target build and Windows self-contained win-x64 publish pass.
- Existing package/WebView/obsolete API warnings remain; no release/version change.
- C# integration runner uses temporary SQLite state and real pinned HTTPS on Windows.
- Checks cover task/payload persistence, stale revisions, missing-revision 428, clone,
  task pages, changes cursors, key revocation, certificate pin rejection and idempotency.
- Upload checks cover wrong offsets, traversal, unconfirmed overwrite, SHA-256,
  native backend folder transfer, empty directories and split UTF-8 output.
- Node tests execute the actual Tasker scripts; added cross-node bulk, local response,
  From Script and payload modal regression cases.
- Final JavaScript suite: 58 passed, zero failures. C# runner: 15 PASS groups,
  including termination of an active HTTPS output stream after revoking its key.
- Published two-process test uses separate executable folders, protected stores and
  SQLite files; browser selects remote node, edits task, creates server folder,
  chooses script, loads Config, then API exercises payload/run/pause/resume/stop.
- Restarting the published server preserves task and node state; local sentinel stays unchanged.
- Edge browser smoke reports no JavaScript page errors; Graphite and Aluminium screenshots inspected.

## Load evidence

15-minute loopback run: 1,000 synthetic running instances, 7,214,000 output chunks,
zero list-request errors, list p95 19.1142 ms. Bridge output remained below 64 MiB;
peak process working set 236,085,248 bytes. A separate one-minute run included
concurrent 4 MiB upload/commit and pause commands: zero errors, list p95 15.4176 ms.
These are synthetic state/output producers, not 1,000 external processes. RTT was
loopback. The 15-minute run predates the final UI and pagination corrections;
the output storage algorithm it measured is unchanged apart from eviction markers.

## Review and limits

Independent review found six initial issues and two follow-up issues. Fixes cover
bulk/confirmation node context, late local responses, From Script transport,
folder prompt, mandatory remote revisions, journal expiration, payload modal context
and pending-only queue pages. A final independent rerun was interrupted by reviewer
account usage limits; direct regression tests and build/browser verification continue.

Public-IP/WAN connectivity and hosting firewall are unverified: no remote server
credentials were supplied. Live PostgreSQL, Linux publication and a real 20 GiB
transfer were not exercised. Safe file publication is advertised only on Windows.
Upload publication is atomic per file; a complete directory tree is not transactional.
The native source picker is covered through its backend selection/transfer test;
the browser smoke does not automate a Windows native file dialog.

The user authorized integration and master was fast-forwarded to `3c97b8b`.
Post-merge JavaScript suite: 58 passed. Windows build succeeded at
`bin/remote-tasker/Cronopus.exe` (0 errors, 9 existing warnings). The normal Debug
output is locked by the user's running Cronopus; that process was left running.
No push, PR, installer release or production deployment occurred.

## Follow-up: integrated firewall and failed-node isolation

- Server Config can open/update, close and inspect its own Windows Firewall rule;
  Apply can enable the rule for the actual bound HTTPS port. Elevation uses Windows
  UAC and a hidden PowerShell process with fixed cmdlets and validated arguments.
- Firewall rule is scoped to one node, TCP port and executable. Close targets only
  that exact rule. Hosting-provider firewall/NAT remains external.
- Windows COM status read and PowerShell `WhatIf` validation passed without changing
  system rules. Real elevated open/close was not executed during verification.
- Browser fixture checked Open/Close/status and Apply's firewall request. Graphite
  and Aluminium screenshots were inspected.
- Browser regression first reproduced local tasks/logs remaining visible on a
  failed remote switch. It now passes immediate clearing, late-SSE rejection and
  return to Local. Resource, instance and output caches are cleared on node change.
- Saved connection `ns3110395` returned a timeout from the running older backend;
  new backend maps that to 504 with a useful connection/firewall message.
- 61 JavaScript tests pass. C# integration suite and Windows target build pass.
  No live hosting-server firewall change or WAN connection success is claimed.

## Follow-up: Check/Create requests and mixed backend versions

- The running workspace backend answered server settings with 200 but the firewall
  route with an empty 404. UI now detects unavailable firewall support and explains
  that the updated application must be started; refreshing static assets is insufficient.
- Real backend browser test first reproduced Create resetting unsaved Enable HTTPS
  and port. After the fix it observed POST /bridge-local/keys 200, displayed the
  returned key, and preserved the unsaved enabled state and port. Test secrets are
  hidden in screenshots and never printed.
- Real Check observed GET /bridge-local/server/firewall 200 and a timestamped
  result. Actions have busy states; server/key feedback appears beside controls.
- The old running backend was also checked read-only in Edge: unsupported Check
  was disabled with an update explanation; Create remained available.
- Added 404 compatibility, duplicate Create suppression and draft-preservation tests.
  Complete Node suite: 64 passed. New and running workspace UI assets synchronized.
# Firewall identity mismatch: actual Windows reproduction

- Reproduced on 2026-10-15 with real elevated rule creation: PowerShell exited 0,
  but COM status reported no rule. NetSecurity `Name` and COM `INetFwRule.Name`
  refer to different fields: the latter is NetSecurity `DisplayName`.
- Both create and update now set DisplayName equal to the stable node rule name.
- Real Windows open/read/update/close runner is opt-in: `--firewall-live`.
- Real Edge + C# backend: `bridge.actions-live.py --firewall-live` passed Apply,
  Check, Close, Open and final rule cleanup. No bridge API mocks in this run.
- The earlier WhatIf-only validation did not prove rule visibility or creation.
- UI keeps failed Open reasons beside the buttons; bridge labels use Cronopus.
- Prepared Windows build: bin/remote-tasker. Installed running app is not replaced.
