# BAS XML control

**Beta in Cronopus 1.2.0.0:** this module is still in development, is not fully tested or feature-complete, and may contain bugs. The Beta designation applies only to BAS; other Cronopus modules retain their stable status.

Open **BAS** in the dock, or `/bas.html`, to run ordinary saved BrowserAutomationStudio XML projects locally or on saved Remote Tasker nodes. Premium, cloud publication and manual compilation are not required.

Local execution requires Windows. Download the prerelease from the [1.2.0.0 Beta release](https://github.com/w3bgr3p/Cronopus/releases/tag/v1.2.0.0); automatic updates continue to select stable releases.

## Run a project

1. Select **Local** or a saved HTTPS connection. Manage connections in **Config → Remote Tasker**.
2. Click **New**, then **Choose XML**. Local opens the Windows file dialog; remote opens the selected node's file browser, showing directories and XML files. Choosing a file fills its path and suggests a name. You can change the name before saving. Cancel leaves the current selection unchanged.
3. Resources load automatically from the XML on the selected node. **From XML** refreshes the form after external source edits. Edit resource fields directly; only changed fields become overrides. **Use XML default** removes an override. The source XML stays untouched.
4. Save and click **Run**. Native BAS thread counts and execution limits remain defined by the XML.
5. Inspect **Runs** and **Log**. **Stop** cancels that project's owned process tree. Other BAS editors and projects are unaffected.

One execution can be active per saved project. Different saved projects can run concurrently. The page does not pretend that a process launch is one native BAS thread.

Supported overrides: `FixedString`, `RandomString`, `FixedInteger` (with its declared range), `LinesFromFile` (path), and single-choice `Select`. Select values come from the XML's `Values`; saving a choice writes its native zero-based `Selected` index in the execution copy. Unknown resource names, undeclared options or unsupported resource types fail validation. Other resource types retain the original XML configuration.

A Select with exactly two `true`/`false` options appears as an On/Off switch. Click or press Space to toggle it. The form still saves the original BAS option strings, preserving native selection mapping; other choice lists remain dropdowns.

The form uses native `Description` captions, hints, defaults and integer bounds. It honors `Visible`, `Enabled`, `IsAdvanced`, multiline/nonempty settings and resource visibility conditions. Hidden implementation resources are omitted; visible native advanced inputs appear through **Advanced**. Native disabled fields and unsupported types stay read-only. Password/token/secret fields use masked inputs. **JSON overrides** is an optional editor synchronized with the form; `{}` means all defaults. Blank text is an explicit empty-string override, not a reset. Save commits the form before Run uses the saved values.

Resource discovery is a read-only `GET /bridge/v1/bas/resources?projectPath=...` on the execution node, available through the same authenticated, certificate-pinned remote connection. Changing XML paths clears previous overrides; changing selection or nodes discards pending form reads so stale resource schemas cannot replace the current form.

The source XML is never overwritten. The runtime receives a copy; relative `LinesFromFile` resource paths are resolved against the source XML directory. Direct `project_path()` calls in executable script code resolve to the original selected XML, allowing ancestor searches for project folders such as `Essentials` and `data`. Strings, comments and canvas metadata are preserved. Relative filesystem paths unrelated to `project_path()` still depend on BAS runtime path semantics; use absolute paths for external files.

## Engine and process lifecycle

The first run downloads the unprotected engine for the XML's `EngineVersion` over HTTPS and verifies its publisher-provided SHA-1 checksum. The archive and pristine engine are shared by version on that node. Per-project runtime folders isolate mutable settings; large executable binaries use NTFS hard links where supported, with copy fallback.

Runtime cloning also preserves empty BAS service directories. Older runtime layouts are repaired when prepared again, without another download or replacement of their settings. Missing service directories can let navigation succeed while browser JavaScript returns an empty string.

The native engine executes `FastExecuteScript.exe --silent`. There is no published-script login, function name or Premium control API. The engine writes normal BAS logs, result files and `bas_reports` under the run's private log directory.

**Release runtime** stops the project's execution, releases its runtime state and clears in-memory run/log history. Release before editing a saved project. **Delete** is available when no execution is active and automatically releases the runtime before removing the saved project; the source XML remains untouched. The prepared engine files remain cached. Source changes are read again on the next run; changing `EngineVersion` requires releasing and preparing the runtime again.

Cronopus owns its execution process tree through a Windows job object. Cancellation or dashboard shutdown closes that tree. Restarting Cronopus does not automatically resume interrupted XML executions.

## Results and network uncertainty

**Run** acknowledges submission, not completion. A successful process exit alone is insufficient: Cronopus requires a native execution report with a successful final message and no failures. Exit code zero without a report is reported as unconfirmed execution, not success.

Remote execution reuses Remote Tasker HTTPS, certificate pins, keys, revocation and operation journaling. If a mutation reply is lost, **Check command** reads the original journal entry without sending the command again. An entry still marked `pending` or `outcome_unknown` does not establish success or failure.

If the reachable node has no journal record, inspect the runs and use **Dismiss pending** after resolving the uncertainty. Dismiss only clears the browser marker; it never reissues the mutation.

Project settings use the existing private `BridgeStore` storage (Windows DPAPI). JSON resource overrides may contain sensitive values; events/errors redact overrides whose names contain `password`, `token` or `secret`. The page displays all logs as text. The authenticated operator can read saved resource overrides.

History is bounded to 250 executions and 500 events per loaded project, 16 KiB per event and 256 KiB in the browser log view. Native output remains for inspection: at most 30 run directories and 1 GiB per project. Before each run, older directories are pruned to reserve space; a running execution is stopped if its output exceeds 512 MiB. Reports exposed through the API are limited to 256 KiB.

The browser decodes native BAS log color markers into theme-aware message colors. Time, thread/type and message appear in separate columns; source file paths are available on hover. Consecutive identical messages collapse with a repetition count and latest timestamp. Native files remain complete. Log content is rendered as text, including HTML-looking messages.

## C# integration

- `BasControlClient`: local XML preparation, owned process lifecycle, log tailing and native report verification.
- `BasXmlProject`: safe XML parsing, resource validation and source preservation.
- `BasControlService`: saved projects, revisions, execution states, cancellation and bounded history.
- `BridgeHost.InvokeBasAsync`: journaled integration under `/bridge/v1/bas/`; the local page uses guarded `/bridge-local/` routes.

## Verification

```powershell
rtk dotnet run --project tests/Bas.Integration/Bas.Integration.csproj
rtk dotnet run --project tests/Bas.Integration/Bas.Integration.csproj -- --live
```

The normal suite uses isolated storage and fake execution over real certificate-pinned HTTPS. Live verification executes the harmless XML smoke fixture in an actual BAS 31.1.0 engine and checks its native report and unchanged source. Set `CRONOPUS_BAS_TEST_ENGINE` to an existing test engine executable to avoid a fresh download.

For browser acceptance, start the suite with `--serve`, then run `rtk python tests/bas_browser.py <BAS_BROWSER_URL>`. The test server prints its actual URL, preferring port 39761.
