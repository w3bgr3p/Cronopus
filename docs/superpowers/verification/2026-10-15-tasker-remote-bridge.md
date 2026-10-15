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

The implementation remains in its isolated branch pending integration choice;
no push, PR, installer release, production deployment or user task execution occurred.
