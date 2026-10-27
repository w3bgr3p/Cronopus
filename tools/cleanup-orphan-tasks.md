# Orphan Tasker process cleanup (Windows)

`cleanup-orphan-tasks.bat` is standalone: copy this one file to the affected server.
It needs only Windows PowerShell and taskkill, both included with Windows.

The default target is `C:\zp_simroute\request_sandbox\running\uber\uber_reg_pr.py`.
It matches python.exe/pythonw.exe with that exact absolute script argument.
It selects a root only when its parent is absent or was created after the root
(the old parent PID has been reused). A live parent excludes the process.

Preview (no processes are terminated):

```bat
cleanup-orphan-tasks.bat
```

Terminate the selected roots and their descendants:

```bat
cleanup-orphan-tasks.bat --apply
```

Use an Administrator command prompt to clean processes from other users/sessions.
For the previously diagnosed group, optionally add `--parent-pid 62532`.
For another script/server, pass its absolute path:

```bat
cleanup-orphan-tasks.bat --script "C:\simroute\request_sandbox\running\uber\uber_reg_pr.py"
cleanup-orphan-tasks.bat --script "C:\simroute\request_sandbox\running\uber\uber_reg_pr.py" --apply
```

Before each termination, process creation time, script and orphan status are
checked again. Repeat the preview afterwards. An orphan may be intentional;
review the list before applying. Termination interrupts current registrations.
The utility cannot identify historical descendants whose Python root has already
exited, and does not claim to recover the original launcher's identity.

Exit codes: 0 = successful preview/cleanup (including an empty list),
1 = a termination failed, 2 = arguments or scanning failed. There are no prompts.

The dashboard's Job Object protection applies to newly launched external tasks;
it does not adopt or terminate old orphans from earlier dashboard processes.
