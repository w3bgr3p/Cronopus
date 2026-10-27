# Remote Tasker

Control another Cronopus Tasker over HTTPS using its IP address. Tasks keep running
on the server when the client disconnects.

## Connect

1. Open server **Config → Remote Tasker** once through RDP.
2. First setup detects the server's public IP automatically; **Detect IP** repeats
   discovery from the server. Enter your RDP IP manually if it differs. **HTTPS port**
   is separate from RDP (default `38473`). Enable HTTPS and Apply.
3. Keep **Open HTTPS port in Windows Firewall when applying** checked, then Apply.
   Approve Windows UAC if prompted. The same block provides **Open HTTPS port**,
   **Close ZenDash rule** and **Check firewall**, using the actual bound port.
   Closing removes only this instance's rule. Hosting firewall/NAT configuration
   is separate and is managed through the hosting provider.
4. Create a named key, then press **Copy connection**. The copied JSON contains
   the HTTPS address, Node ID, certificate SHA-256 fingerprint and access key.
5. In local Tasker's node selector choose **+ New connection…** and paste the JSON
   into **Connection code**. Review the filled fields, then **Add connection**.
   The connection is saved and selected immediately. You can also enter the fields
   manually; a bare IP uses HTTPS port `38473`, and `IP:port` uses that port.
   Config → Remote Tasker → Connections also supports adding and testing connections.

The key is shown only when created; copy the connection before leaving or reloading
the page. Treat the connection code as a secret: it grants remote Tasker access.
**Revoke** disables a key and interrupts its streams and uploads. **Clear revoked
keys** removes only already revoked entries and preserves active keys. Editing a
saved connection with an empty key field keeps its existing key.

An occupied port falls back to an OS-selected port. Config displays the actual URL;
save that port for a stable next startup. No domain is required. The client checks
certificate pin, validity, IP and node identity. Existing Task API tokens do not
grant bridge access. Changing server IP renews the certificate and requires updating
client fingerprints. Revoking a key interrupts its streams and uploads.

## Tasks and files

Settings, payload, From Script, scheduling, run/stop, pause/resume, cloning and queue
controls operate on the selected server. Scheduling is evaluated by the server;
the API reports UTC. Stale edits are rejected: reload before saving again.
Switching nodes stops remaining bulk steps and asks about unsaved changes.
Native editor and terminal commands remain local.

**Upload file / Upload folder** selects a native local source, then a server destination.
Server drives are accessible within the server process's Windows permissions.
Folders retain their tree and empty directories; links are skipped. Existing files
require explicit replacement confirmation. Use a new folder for an executing
project. Uploading does not edit or restart tasks. The result shows **Saved on
server** and **Left unchanged** counts; declining replacement leaves that file
unchanged. **Use another script in task…** sets the form path for an explicit save.
To use an updated script in a running task, stop and run the task again.

Transfers use 4 MiB blocks and SHA-256, support cancellation and resume, and expire
after 24 hours. Publication is atomic per file, not per tree. Limits: 10,000 files,
20 GiB per file, default 40 GiB staging quota and 8 MiB/s. Quota and rate are
configurable. Safe upload publication currently requires a Windows server.

## Logs and connection failures

Refresh (↺) / Older / Observe are explicit; no log stream starts automatically. Pages are
limited to 200 entries / 256 KiB. Each key gets one stream capped at 64 KiB/s.
The browser retains 2,000 entries; the bridge buffer is capped at 64 MiB and
15 minutes. Gaps are marked. Saved output is read as a bounded tail.

For an unknown command result, use Check commands after reconnecting. Do not
automatically repeat a run: it may already have executed. Results are retained
for seven days. Build/install operations expose bounded progress.

Renew certificate creates a replacement certificate; update all client pins.
Windows state uses current-user DPAPI encryption and cannot simply be moved to
another Windows account. Redirects and mismatching certificates are rejected.
Run `Cronopus.exe --headless` to start without opening the panel; this does not
install a Windows service. Check the actual URL, firewall, IP, expiry and key
when a connection fails.

Switching nodes immediately clears previous tasks, statistics and output. A failed
remote connection shows an error and an empty remote view. Choose Local to restore
the local task list.
