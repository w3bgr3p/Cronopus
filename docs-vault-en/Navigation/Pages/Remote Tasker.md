# Remote Tasker

Control another z3nDash Tasker over HTTPS using its IP address. Tasks keep running
on the server when the client disconnects.

## Connect

1. Open server **Config → Remote Tasker** once through RDP.
2. First setup detects the server's public IP automatically; **Detect IP** repeats
   discovery from the server. Enter your RDP IP manually if it differs. **HTTPS port**
   is separate from RDP (default `38473`). Enable HTTPS and Apply.
3. Allow the actual port through the server and hosting firewall.
4. Create a named key. Copy the key, Node ID and certificate SHA-256 fingerprint.
5. In local Tasker's node selector choose **+ New connection…**. Enter a name,
   `https://IP:port`, Node ID, pin and key, then **Add connection**. It is saved
   and selected immediately. Config → Remote Tasker → Connections also supports
   adding and testing connections.

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

Upload file/folder selects a native local source, then a server destination.
Server drives are accessible within the server process's Windows permissions.
Folders retain their tree and empty directories; links are skipped. Existing files
require explicit replacement confirmation. Use a new folder for an executing
project. Uploading does not edit or restart tasks. Choose uploaded script sets the
form path for an explicit save.

Transfers use 4 MiB blocks and SHA-256, support cancellation and resume, and expire
after 24 hours. Publication is atomic per file, not per tree. Limits: 10,000 files,
20 GiB per file, default 40 GiB staging quota and 8 MiB/s. Quota and rate are
configurable. Safe upload publication currently requires a Windows server.

## Logs and connection failures

Load / Older / Observe are explicit; no log stream starts automatically. Pages are
limited to 200 entries / 256 KiB. Each key gets one stream capped at 64 KiB/s.
The browser retains 2,000 entries; the bridge buffer is capped at 64 MiB and
15 minutes. Gaps are marked. Saved output is read as a bounded tail.

For an unknown command result, use Check commands after reconnecting. Do not
automatically repeat a run: it may already have executed. Results are retained
for seven days. Build/install operations expose bounded progress.

Renew certificate creates a replacement certificate; update all client pins.
Windows state uses current-user DPAPI encryption and cannot simply be moved to
another Windows account. Redirects and mismatching certificates are rejected.
Run `z3nDash.exe --headless` to start without opening the panel; this does not
install a Windows service. Check the actual URL, firewall, IP, expiry and key
when a connection fails.
