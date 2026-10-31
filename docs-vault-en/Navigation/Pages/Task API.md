# Controlling a task from a script

Every run gets its own context. The API address is shared; the task and run IDs are
determined automatically. One file can serve several tasks. There is no need to look a
task up by name or read its ID from the database.

## Python

The `cronopus` module ships with the application. The scheduler adds it to `PYTHONPATH`
automatically, including runs inside a venv. Installing the package with pip is not
needed.

```python
from cronopus import current_task

try:
    do_work()
except ServiceUnavailable:
    current_task.defer(seconds=3600, reason="Service unavailable")
    raise
```

`defer()` does not end the script and does not swallow errors. Once the deferral is
confirmed, the script decides for itself whether to finish, release resources or raise.
If the API is unreachable the helper raises: a deferral must not be assumed to be set.

```python
state = current_task.get()       # task_id, run_id, name and the schedule state
current_task.pause()             # Pause automatic runs indefinitely
current_task.resume()            # Lift the pause and any temporary deferral
current_task.defer(until="2026-12-01T12:00:00Z", reason="Until a given time")
```

## C# and XML OwnCode

In `csx-internal` and in XML OwnCode blocks the `current_task` object is available:

```csharp
await current_task.DeferAsync(3600, "Service unavailable");
var state = await current_task.GetAsync();
```

In a synchronous block: `current_task.DeferAsync(3600).GetAwaiter().GetResult();`.
Inside the application's own handlers the same context is available as
`TaskRunContext.Current`. The context is isolated between parallel runs.

## HTTP API

External processes receive `CRONOPUS_API_URL`, `CRONOPUS_RUN_TOKEN`, `CRONOPUS_TASK_ID`
and `CRONOPUS_RUN_ID` through the environment. The address carries the actual port of the
running application. For `/self` the address and the token are enough.
Every request carries the `Authorization: Bearer <CRONOPUS_RUN_TOKEN>` header.
The token is valid only until the run ends. Do not print it to logs.

| Method | Path | Body |
|---|---|---|
| GET | `/api/v1/self` | — |
| POST | `/api/v1/self/defer` | `{"delay_seconds":3600,"reason":"Service error"}` |
| POST | `/api/v1/self/defer` | `{"until":"2026-12-01T12:00:00Z"}` |
| POST | `/api/v1/self/pause` | `{}` |
| POST | `/api/v1/self/resume` | `{}` |

Every command answers with the state: `task_id`, `run_id`, `name`, `enabled`, `paused`,
`deferred_until` (UTC or null), `reason`, `schedule_mode`. The token is not part of the
answer. `delay_seconds` is a positive number of seconds from the moment of the request.
`until` is a future time with `Z` or a UTC offset. The two fields cannot be sent together.
A reason is optional, 2000 characters at most. Errors: 400 — bad parameters,
401 — missing or finished context, 404 — task or command not found,
503 — database not connected, 500 — internal error. The error body is `{"error":"..."}`.

A Node.js example:

```javascript
const response = await fetch(process.env.CRONOPUS_API_URL + '/api/v1/self/defer', {
  method: 'POST',
  headers: { 'Content-Type': 'application/json',
             Authorization: 'Bearer ' + process.env.CRONOPUS_RUN_TOKEN },
  body: JSON.stringify({ delay_seconds: 3600, reason: 'Service unavailable' })
});
if (!response.ok) throw new Error(await response.text());
```

## Controlling other tasks by ID

A watcher script can pause and defer other tasks. It uses the same run token as
`/self`; the target task's ID goes in the path.

```python
from cronopus import task, list_tasks

for state in list_tasks(name="direction_1"):    # exact name match
    task(state["task_id"]).defer(seconds=1800, reason="watcher: no new records")

task("a1b2c3").pause()
task("a1b2c3").resume()
```

| Method | Path | Body |
|---|---|---|
| GET | `/api/v1/tasks` | — (optional `?name=` — exact name) |
| GET | `/api/v1/tasks/{id}` | — |
| POST | `/api/v1/tasks/{id}/defer` | same as `/self/defer` |
| POST | `/api/v1/tasks/{id}/pause` | `{}` |
| POST | `/api/v1/tasks/{id}/resume` | `{}` |

The response is the same state as for `/self` (a list of states for `GET /api/v1/tasks`),
with `run_id` set to `null`. Error codes are the same; 404 means no task has that ID.
Deferral rules are shared: competing deferrals keep the latest time, and `resume`
clears both the pause and the deferral.

## How the schedule behaves

- A deferral applies to the whole task. Instances already admitted keep running; new
  automatic runs and the queue wait.
- Reserved instances waiting on a start delay check the pause as well.
- Deferrals and indefinite pauses are stored in the database and survive a restart.
- Competing deferrals pick the latest time. Shortening it takes an explicit `resume`
  followed by a new `defer`.
- Once a deferral expires the previous schedule and its limits apply again. It is a ban
  on starting before the given time, not a promise to start exactly at that second. The
  scheduler checks readiness once a minute.
- A manual `Run` starts the task at once and does not lift the deferral. A manual run
  that lands in the queue because threads are busy waits along with the queue.
- `resume` lifts the pause and the deferral but changes neither `enabled` nor the
  schedule mode. A disabled task has to be enabled separately.
- Tasker shows the deferral deadline and a `Resume schedule` button to lift it.

## Driving the application from outside

External agents should use the native [[MCP]] tools to discover, create, update and
run tasks. The following is the browser HTTP contract for integrations that use
the dashboard API. It uses the running dashboard's address and existing access;
the per-run bearer token described above belongs to `/api/v1`, not this contract.
Send POST bodies as JSON with `Content-Type: application/json`.

### Reading and identifying a task

`GET /tasker/list` returns an array of saved task records, excluding `last_output`.
Identify the source task from that list, then read `GET /tasker/detail?id=TASK_ID`.
Detail returns the saved fields and `_revision`; use its exact value for updates,
payload changes and deletion. A script path is not a unique task identifier.

### Creating and updating: POST /tasker/save

Omit `id` (or send an empty string) to create a task with a generated ID. A non-empty
`id` updates that record if it exists, but **inserts a record if it does not**.
For an update, send only the fields to change: omitted fields keep their values.
Always include the latest `_revision` when editing an existing task.

All task fields in this HTTP body are **strings**, including booleans, numbers and
encoded JSON. MCP tool settings use their own typed schemas instead.

| Fields | HTTP values |
|---|---|
| `name`, `executor`, `script_path`, `args`, `schedule_tag` | Strings; use an absolute script path on the target node |
| `enabled`, `use_venv` | `"true"` or `"false"` |
| `max_threads`, `timeout_seconds` | Integer strings; timeout `"0"` means unlimited |
| `schedule_mode` | `"off"` (manual), `"cron"` or `"zp"` |
| `cron` | Cron expression string |
| `schedule_json`, `browser_json` | JSON object encoded as a string |
| `on_overlap` | `"skip"`, `"parallel"` or `"kill_restart"` |
| `payload_schema`, `payload_values` | JSON array / object encoded as strings |
| `interval_minutes`, `fixed_time` | Stored schedule fields as strings |

The handler also accepts `runs_total`, `runs_success`, `last_run_id`, `sched_runs`
and `sched_started_at`; omit these when creating a copy so its run history and
schedule progress are not inherited. Runtime fields `status`,
`last_run`, `last_exit` and `last_output` are ignored by save.

Create a disabled manual task:

```json
{
  "name": "Example.Worker",
  "executor": "python",
  "script_path": "W:\\scripts\\worker.py",
  "args": "",
  "enabled": "false",
  "use_venv": "false",
  "schedule_mode": "off",
  "on_overlap": "skip",
  "max_threads": "1",
  "timeout_seconds": "0"
}
```

Success returns `{"ok":true,"id":"NEW_ID","_revision":"OPAQUE_REVISION"}`.
Use the returned ID, never the source task's ID, for the new task's payload.
An update body can be as small as:

```json
{"id":"TASK_ID","_revision":"OPAQUE_REVISION","max_threads":"2"}
```

Saving evaluates the schedule immediately. Keep a new copy disabled while setting
its payload; enabling a due schedule can start it before a separate manual Run.
A revision mismatch returns HTTP 409 with `code: "revision_conflict"`; reload detail
and reconcile the change before saving again. Without `_revision` this protection
is skipped. Check both the HTTP status and `ok`; a failed write can return
`{"ok":false,"error":"step=insert | ..."}` (or `step=update` / `step=verify`).

### Payload: GET /tasker/payload and POST /tasker/payload

`GET /tasker/payload?id=TASK_ID` returns `{"id":"TASK_ID","schema":"...","values":"..."}`.
`schema` is a JSON array encoded as a string; `values` is a JSON object encoded as
a string. Preserve these strings when copying. The GET response has no revision;
read detail for the revision of an existing task.

POST replaces **both** payload fields; an omitted field becomes an empty string.
The task must already exist (otherwise HTTP 404). Example body:

```json
{
  "id": "NEW_ID",
  "_revision": "OPAQUE_REVISION",
  "schema": "[]",
  "values": "{\"country\":\"MX\"}"
}
```

Success returns `{"ok":true,"_revision":"NEW_REVISION"}`. Use that revision for
the next edit. To clear the payload, send `schema: ""` and `values: ""` together.
`POST /tasker/args-preview` accepts `{"id":"TASK_ID"}` and optional `schema` and
`values` strings to preview unsaved payload. Omitted fields use the saved payload.
It returns `ok`, a masked `command` and `skipped`, or `ok:false`, `step`, `error`;
XML tasks return a note because payload is passed to project variables.

### Copying a task

1. Read the source detail and payload using its discovered ID.
2. POST `/tasker/save` with a new name, the desired settings and `enabled: "false"`.
   Omit the source `id`, `_revision` and run-history fields.
3. POST `/tasker/payload` with the **new** ID and revision from step 2, and the
   source's `schema` / `values` strings. Check that this save succeeded.
4. Read back the new detail and payload. Enable or run the copy only after checking
   its target path, settings and payload. The script file itself is not copied.

There is no dedicated HTTP duplicate route. These are separate writes; if payload
saving fails, the new task still exists. Repair that task instead of creating
another copy. If a create response is lost, inspect the task list before retrying.

### Run, stop, delete and runtime

| Method and path | JSON body or query |
|---|---|
| POST `/tasker/run` | `{"id":"TASK_ID","count":3,"args":"--country MX"}`; `count` and `args` optional |
| POST `/tasker/stop` | `{"id":"TASK_ID"}`; stops active instances |
| POST `/tasker/delete` | `{"id":"TASK_ID","_revision":"OPAQUE_REVISION"}`; stops instances and deletes the task |
| GET `/tasker/instances` | `?id=TASK_ID`; active instances and their `runId` values |
| POST `/tasker/kill-instance` | `{"id":"TASK_ID","runId":"RUN_ID"}`; omitting `runId` stops all instances |
| GET `/tasker/queue` | `?id=TASK_ID`; queued runs |
| POST `/tasker/clear-queue` | `{"id":"TASK_ID"}` |
| GET `/tasker/output/stream` | `?id=TASK_ID`; SSE output stream |
| GET `/tasker/live-output` | `?id=TASK_ID&runId=RUN_ID`; `runId` optional; returns `id`, `isLive`, `output`, `result` |

Run returns `{"ok":true,"id":"TASK_ID","queued":3,"args":"--country MX"}`.
This confirms queue admission, not completion. `count` defaults to 1 and accepts
an integer or integer string from 1 to 10000. Use instances, queue and output to
establish the result. Stop, delete, kill-instance and clear-queue return `{"ok":true}`.

`/tasker/run` also takes `count` (how many runs to queue) and an optional string `args`:
it goes into the queue items of that burst instead of the task's `args`, and the task
itself is not changed. This lets an orchestrating script launch a burst with a different
country without touching the schedule or settings. Payload flags are still appended,
except those already present in `args`. A non-string or empty `args` gets 400
`args must be a non-empty string` (checked before `count` and before the task lookup).
The response includes the burst's `args`.

There are also operator commands: `POST /tasker/pause` and `/tasker/resume` with the
body `{"id":"..."}`, and `POST /tasker/defer?id=...` with the same body as
`/self/defer`. They use the existing access to the dashboard API.
To control its own task from a script, use `/self`: no ID is needed there.
