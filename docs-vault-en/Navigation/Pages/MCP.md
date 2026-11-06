# MCP for agents

Cronopus includes a native stdio MCP server: `Cronopus.exe --mcp`.
No Python, Node.js or separate MCP package is needed. Start the updated
dashboard normally before using tools that access it.

## Connect

Configure an MCP client using the absolute path to the installed executable:

```json
{
  "mcpServers": {
    "cronopus": {
      "command": "C:\\Apps\\Cronopus\\Cronopus.exe",
      "args": ["--mcp"]
    }
  }
}
```

Replace the example path. Discovery reads `.runtime/mcp.json` next to the EXE,
which contains the actual dashboard port, including fallback ports.
Use `--api-url http://localhost:YOUR_PORT` to select an instance explicitly.
Only loopback HTTP origins are accepted. Add `--read-only` to hide write tools.

Agent documentation is English. Use tool schemas directly; do not preload guides.
`read_document(topic=mcp)` provides a short contract when needed. `list_tasks` discovers
IDs; `get_task` provides settings and `_revision` for protected update/delete.
On conflict, reload and review. `run_task` queues real executions; verify them
using `list_task_runtime` and `read_task_output`.

`list_nodes` lists saved [[Remote Tasker]] connections. Pass the connection ID
as `node`; the default is `local`. Credentials remain in the bridge store.
MCP exposes Tasker; terminal, database editors and secret configuration are
outside this tool catalog.

## File hashes (1.2.0.0)

`get_file_hashes(root, paths, node="local")` reads files without modifying them. `root` must be an absolute folder on the selected node; `paths` contains at most 64 distinct relative file paths. Binary files are supported. The reply contains `root` and `items`: each item has `path` and `status` (`ok`, `missing` or `error`). An `ok` item includes `sha256`, `size` in bytes and `modified_utc`; an error includes `error`. Empty `paths` checks the folder without enumerating it. Paths outside the root and paths through links are rejected.

Remote nodes must advertise the `file-hashes` capability; update the remote Cronopus if it is missing. The tool is available in `--read-only` mode. The Bridge equivalent is `POST /bridge/v1/files/hashes` with `{"root":"C:\\Projects","paths":["job.xml"]}` and the existing authenticated connection. MCP does not expose BAS control tools in this release; use [[BAS]] for those operations.

The compact English contract is `cronopus://docs/mcp` / `Mcp/AGENT.md`.
Setup, TOML configuration and full reference: `read_document(topic=setup)`
or `Mcp/README.md` alongside the application.
[[Task API]] and [[Script agent instructions]] are available through
`read_document` with topic `task-api` / `script-agent` (English only).
Documentation remains accessible when the dashboard is offline.

After a mutation timeout, inspect state before retrying. `stop_task` kills
running processes; `delete_task` removes the task and stops its executions,
while preserving script files.
