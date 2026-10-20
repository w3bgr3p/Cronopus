# MCP for agents

z3nDash includes a native stdio MCP server: `z3nDash.exe --mcp`.
No Python, Node.js or separate MCP package is needed. Start the updated
dashboard normally before using tools that access it.

## Connect

Configure an MCP client using the absolute path to the installed executable:

```json
{
  "mcpServers": {
    "z3ndash": {
      "command": "C:\\Apps\\z3nDash\\z3nDash.exe",
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

The compact English contract is `z3ndash://docs/mcp` / `Mcp/AGENT.md`.
Setup, TOML configuration and full reference: `read_document(topic=setup)`
or `Mcp/README.md` alongside the application.
[[Task API]] and [[Script agent instructions]] are available through
`read_document` with topic `task-api` / `script-agent` (English only).
Documentation remains accessible when the dashboard is offline.

After a mutation timeout, inspect state before retrying. `stop_task` kills
running processes; `delete_task` removes the task and stops its executions,
while preserving script files.
