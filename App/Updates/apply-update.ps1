param([Parameter(Mandatory=$true)][string]$Request)
$ErrorActionPreference = 'Stop'
$requestData = Get-Content -LiteralPath $Request -Raw | ConvertFrom-Json
$targetRoot = [IO.Path]::GetFullPath($requestData.Target).TrimEnd('\') + '\'
$payloadRoot = [IO.Path]::GetFullPath($requestData.Payload).TrimEnd('\') + '\'
$updateRoot = [IO.Path]::GetFullPath($requestData.Root).TrimEnd('\') + '\'
$activation = [IO.File]::Open((Join-Path $updateRoot 'activation.lock'), 'OpenOrCreate', 'ReadWrite', 'None')
$backupRoot = Join-Path $updateRoot ('backup-' + [guid]::NewGuid().ToString('N'))
$changed = [Collections.Generic.List[object]]::new()
$rollbackFailed = $false
$dashboardRunning = $false
$installationBusy = $false
try {
    if (!$payloadRoot.StartsWith($updateRoot, [StringComparison]::OrdinalIgnoreCase) -or $targetRoot -eq $payloadRoot) {
        throw 'Invalid update paths.'
    }
    $parent = Get-Process -Id $requestData.ParentId -ErrorAction SilentlyContinue
    if ($parent -and !$parent.WaitForExit(30000)) { throw 'Previous startup process did not exit. Update postponed.' }
    # Never stop processes. A second dashboard or an MCP client postpones activation.
    foreach ($process in @(Get-Process -Name Cronopus -ErrorAction SilentlyContinue)) {
        if ($process.Path -and [IO.Path]::GetFullPath($process.Path).StartsWith($targetRoot, [StringComparison]::OrdinalIgnoreCase)) {
            $installationBusy = $true
            $command = (Get-CimInstance Win32_Process -Filter ('ProcessId = ' + $process.Id) -ErrorAction SilentlyContinue).CommandLine
            if (!$command -or $command -notmatch '(?i)(^|\s)"?--mcp"?(\s|$)') { $dashboardRunning = $true }
        }
    }
    if ($installationBusy) { throw 'Another Cronopus or MCP client is using this installation. Update postponed; close it before your next manual start.' }
    $files = @(Get-ChildItem -LiteralPath $payloadRoot -File -Recurse -Force)
    if (!$files.Count) { throw 'Update payload is empty.' }
    # Probe every existing target before changing the installation.
    foreach ($item in @(Get-ChildItem -LiteralPath $payloadRoot -Recurse -Force)) {
        if ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Update contains a link.' }
    }
    foreach ($file in $files) {
        if ($file.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Update contains a link.' }
        $relative = $file.FullName.Substring($payloadRoot.Length)
        $destination = Join-Path $targetRoot $relative
        $walk = $destination
        while ($walk.Length -ge $targetRoot.TrimEnd('\').Length) {
            if ((Test-Path -LiteralPath $walk) -and ((Get-Item -LiteralPath $walk -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)) {
                throw 'Installation contains a link at an update destination.'
            }
            $walk = [IO.Path]::GetDirectoryName($walk)
        }
        if (Test-Path -LiteralPath $destination) {
            $probe = [IO.File]::Open($destination, 'Open', 'ReadWrite', 'None')
            $probe.Dispose()
        }
    }
    New-Item -ItemType Directory -Path $backupRoot | Out-Null
    foreach ($file in $files) {
        $relative = $file.FullName.Substring($payloadRoot.Length)
        $destination = Join-Path $targetRoot $relative
        $backup = Join-Path $backupRoot $relative
        $existed = Test-Path -LiteralPath $destination
        if ($existed) {
            [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($backup)) | Out-Null
            [IO.File]::Copy($destination, $backup, $true)
        }
        $changed.Add([pscustomobject]@{Destination=$destination; Backup=$backup; Existed=$existed})
        [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($destination)) | Out-Null
        [IO.File]::Copy($file.FullName, $destination, $true)
    }
    Remove-Item -LiteralPath (Join-Path $updateRoot 'pending.json')
    Remove-Item -LiteralPath (Join-Path $updateRoot 'apply-error.txt') -ErrorAction SilentlyContinue
} catch {
    $failure = $_.Exception.Message
    for ($i = $changed.Count - 1; $i -ge 0; $i--) {
        $entry = $changed[$i]
        try {
            if ($entry.Existed) { [IO.File]::Copy($entry.Backup, $entry.Destination, $true) }
            elseif (Test-Path -LiteralPath $entry.Destination) { [IO.File]::Delete($entry.Destination) }
        } catch { $rollbackFailed = $true; $failure += "`r`nRollback: " + $_.Exception.Message }
    }
    [IO.File]::WriteAllText((Join-Path $updateRoot 'apply-error.txt'), $failure)
}
if (!$rollbackFailed -and !$dashboardRunning) {
    $launch = [Diagnostics.ProcessStartInfo]::new()
    $launch.FileName = $requestData.Executable
    $launch.Arguments = $requestData.Arguments
    $launch.WorkingDirectory = $targetRoot
    $launch.UseShellExecute = $false
    $launch.CreateNoWindow = $true
    [Diagnostics.Process]::Start($launch) | Out-Null
} elseif ($rollbackFailed) {
    Add-Type -AssemblyName System.Windows.Forms
    [Windows.Forms.MessageBox]::Show('Update rollback failed. Your backup is in ' + $backupRoot + '. See ' + (Join-Path $updateRoot 'apply-error.txt'), 'Cronopus update') | Out-Null
}
$activation.Dispose()
