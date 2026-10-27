@echo off
setlocal
set "Z3N_CLEANUP_SELF=%~f0"
set "Z3N_CLEANUP_APPLY=0"
set "Z3N_CLEANUP_SCRIPT=C:\zp_simroute\request_sandbox\running\uber\uber_reg_pr.py"
set "Z3N_CLEANUP_PARENT="
:parse
if "%~1"=="" goto run
if /i "%~1"=="--apply" (
    set "Z3N_CLEANUP_APPLY=1"
    shift
    goto parse
)
if /i "%~1"=="--script" (
    if "%~2"=="" goto usageError
    set "Z3N_CLEANUP_SCRIPT=%~2"
    shift
    shift
    goto parse
)
if /i "%~1"=="--parent-pid" (
    if "%~2"=="" goto usageError
    set "Z3N_CLEANUP_PARENT=%~2"
    shift
    shift
    goto parse
)
if /i "%~1"=="--help" goto help
goto usageError
:help
echo Usage: %~nx0 [--script "C:\absolute\script.py"] [--parent-pid NUMBER] [--apply]
echo Default: preview orphan Uber processes. --apply terminates their process trees.
exit /b 0
:usageError
echo Invalid arguments. Run %~nx0 --help. 1>&2
exit /b 2
:run
powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -Command "$s=[IO.File]::ReadAllText($env:Z3N_CLEANUP_SELF); $marker='# Z3N_POWERSHELL'+'_START'; & ([scriptblock]::Create($s.Substring($s.IndexOf($marker))))"
exit /b %errorlevel%
# Z3N_POWERSHELL_START
$ErrorActionPreference = 'Stop'
try {
    $scriptPath = $env:Z3N_CLEANUP_SCRIPT
    if ($scriptPath -notmatch '^(?:[A-Za-z]:\\|\\\\)' -or
        [IO.Path]::GetExtension($scriptPath) -ne '.py') {
        throw '--script must be an absolute path to a Python script.'
    }
    $scriptPath = [IO.Path]::GetFullPath($scriptPath)
    $parentFilter = 0
    if ($env:Z3N_CLEANUP_PARENT -and
        (![int]::TryParse($env:Z3N_CLEANUP_PARENT, [ref]$parentFilter) -or $parentFilter -le 0)) {
        throw '--parent-pid must be a positive integer.'
    }
    $apply = $env:Z3N_CLEANUP_APPLY -eq '1'
    $quotedPath = [regex]::Escape($scriptPath)
    $pattern = '(?i)(?:^|\s)(?:"' + $quotedPath + '"|' + $quotedPath + ')(?=\s|$)'
    $all = @(Get-CimInstance Win32_Process)
    $byId = @{}
    foreach ($p in $all) { $byId[[int]$p.ProcessId] = $p }
    $orphans = @(
        foreach ($p in $all) {
            if ($p.Name -notin @('python.exe', 'pythonw.exe') -or $p.CommandLine -notmatch $pattern) { continue }
            if ($parentFilter -and $p.ParentProcessId -ne $parentFilter) { continue }
            $parent = $byId[[int]$p.ParentProcessId]
            if (!$parent -or $parent.CreationDate -gt $p.CreationDate) { $p }
        }
    )
    Write-Output "Script: $scriptPath"
    Write-Output "Orphan roots: $($orphans.Count)"
    $orphans | Select-Object ProcessId, ParentProcessId, CreationDate | Format-Table -AutoSize
    if (!$apply) {
        Write-Output 'PREVIEW ONLY. Repeat with --apply to terminate the listed roots and their descendants.'
        exit 0
    }
    $failed = $false
    foreach ($p in $orphans) {
        $current = Get-CimInstance Win32_Process -Filter "ProcessId = $($p.ProcessId)"
        if (!$current -or $current.CreationDate -ne $p.CreationDate -or
            $current.CommandLine -notmatch $pattern -or $current.ParentProcessId -ne $p.ParentProcessId) {
            Write-Output "SKIP $($p.ProcessId): process identity changed or already exited."
            continue
        }
        $parent = Get-CimInstance Win32_Process -Filter "ProcessId = $($current.ParentProcessId)"
        if ($parent -and $parent.CreationDate -le $current.CreationDate) {
            Write-Output "SKIP $($p.ProcessId): parent is alive."
            continue
        }
        & "$env:SystemRoot\System32\taskkill.exe" /PID $current.ProcessId /T /F
        if ($LASTEXITCODE -ne 0) { $failed = $true; continue }
        $remaining = Get-CimInstance Win32_Process -Filter "ProcessId = $($p.ProcessId)"
        if ($remaining -and $remaining.CreationDate -eq $p.CreationDate) {
            [Console]::Error.WriteLine("FAIL $($p.ProcessId): root still running.")
            $failed = $true
        }
    }
    if ($failed) { exit 1 }
    exit 0
} catch {
    [Console]::Error.WriteLine($_.Exception.Message)
    exit 2
}
