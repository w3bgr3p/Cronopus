# Config

HTTPS bridge between instances: **Remote Tasker** section, see [[Remote Tasker]].

The `/config.html` page manages the local Cronopus configuration.

The side rail switches between Overview, Database, Logs & Server, Browsers API,
Services, Remote Tasker, Security, Maintenance, Interface and About. The last open section is restored
on the next visit.

## About and updates

About shows the installed version, license, source code and release notes.
On Windows, automatic checks run 30 seconds after startup and every six hours;
the checkbox also enables automatic downloading. Use **Check for updates** or
**Download update** to prepare a release manually.

The updater downloads the stable GitHub portable package and verifies its SHA-256.
It extracts the new version into a separate directory. The running application,
Tasker instances and MCP clients continue working. Once ready, About displays:
**Restart the program when convenient to apply the update.** There is no automatic restart.

Exit Cronopus yourself after your tasks finish, then start it again. Before the
scheduler starts, a separate helper applies the prepared files and launches the
new version. If another dashboard, MCP client or locked file prevents activation,
the update stays pending. Close the conflicting process before your next manual
start. Copy failures restore the previous files from a backup; configuration,
databases, logs and interface state are preserved.

Prepared packages, backups and activation errors are stored per installation in
`%LOCALAPPDATA%/Cronopus/updates/`. Older releases without an updater need one
manual installation of a version that includes this feature.

## Server Status

Shows:

- the configuration state;
- the database connection;
- the dashboard port;
- the log and report folders;
- the selected database mode.

The data is loaded through `GET /config/status`.

## Database

Two modes are supported:

- PostgreSQL — a single connection string field;
- SQLite — a path to a file.

For PostgreSQL the schema comes from `Search Path`. The connection string can be copied with the button beside the field.

## Logs & Server

The dashboard port and the working folders are set here.

The server listens on `localhost` only. Do not change that: the routes require no
authentication and allow starting processes and reading secrets.

## Browsers API

Local API address and token for two browsers:

- **ZennoBrowser** — `http://localhost:8160` by default, the token is sent in the `Api-Token` header;
- **ShardX** — `http://127.0.0.1:40325` by default, the token (the permanent JWT from the
  launcher settings) is sent as `Authorization: Bearer`.

The [[Browser]] page and xml tasks with the `shardx` browser mode use these settings.

## Security

The jVars section stores encrypted local variables and the path to the jVars file.

## Interface

- the theme: Graphite, Aluminium, Hyper or Paper (`Alt+T` cycles through them);
- the screen edge for the [[Nav Dock]] (`Alt+P` moves it to the next edge);
- **Dock pages** — which pages the dock shows. Config cannot be removed from the
  dock, otherwise the dock would offer no way back to the settings.

Changes apply at once, with no save button, and affect every page. They are kept
in `ui-state.json` next to the executable: `theme`, `dockPosition` and
`dockHidden`, the list of hidden pages. A page added to `navpath.js` later shows
up in the dock on its own.

`POST /config/ui` merges the posted fields into the stored ones, so a request may
carry only the field that changed.

The machine tool check (Environment) has moved to the [[System Snapshot]] page.

## Memory Watchdog

Configured here:

- the root process name;
- whether the watchdog is on;
- a memory limit in MB;
- a check interval.

The watchdog counts the memory of the root process together with its child tree. Once a non-zero limit is exceeded, the process tree is terminated.

## Storage

Shows the size and the number of files in the log folder — including `trafficLog.jsonl`
and its rotated copies.

There is a single cleanup, `POST /clear-all-logs`, which clears the whole folder.

## Import

Available imports:

- proxies;
- EVM addresses;
- SOL addresses.

They use `POST /import/proxy` and `POST /import/addresses`.

## API

- `GET /config`
- `POST /config`
- `GET /config/status`
- `GET /config/storage`
- `POST /config/jvars`
- `GET /config/ui`
- `POST /config/ui`
