$ErrorActionPreference = 'Stop'
$testRoot = Join-Path ([IO.Path]::GetTempPath()) ('cronopus-activation-test-' + [guid]::NewGuid().ToString('N'))
$helper = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../App/Updates/apply-update.ps1'))
[IO.Directory]::CreateDirectory($testRoot) | Out-Null
function Assert($condition, $message) { if (!$condition) { throw $message } }
function Scenario($name) {
    $root = Join-Path $testRoot $name
    $target = Join-Path $root 'installation'
    $updates = Join-Path $root 'updates'
    $payload = Join-Path $updates 'payload'
    [IO.Directory]::CreateDirectory($target) | Out-Null
    [IO.Directory]::CreateDirectory($payload) | Out-Null
    $marker = Join-Path $root 'launched.txt'
    $request = Join-Path $root 'request.json'
    @{
        ParentId=2147483647; Target=$target; Payload=$payload; Root=$updates
        Executable=(Join-Path $PSHOME 'powershell.exe')
        Arguments=('-NoProfile -NonInteractive -Command "[IO.File]::WriteAllText(''' + $marker + ''',''started'')"')
    } | ConvertTo-Json | Set-Content -LiteralPath $request -Encoding UTF8
    '{}' | Set-Content -LiteralPath (Join-Path $updates 'pending.json')
    return @{Root=$root; Target=$target; Updates=$updates; Payload=$payload; Request=$request; Marker=$marker}
}
function Apply($s) {
    & (Join-Path $PSHOME 'powershell.exe') -NoProfile -NonInteractive -ExecutionPolicy Bypass -File $helper -Request $s.Request
    Assert ($LASTEXITCODE -eq 0) 'Helper failed unexpectedly'
    for ($i = 0; $i -lt 50 -and !(Test-Path -LiteralPath $s.Marker); $i++) { Start-Sleep -Milliseconds 100 }
    Assert (Test-Path -LiteralPath $s.Marker) 'Dashboard was not launched after manual activation'
}
try {
    $s = Scenario 'success'
    [IO.File]::WriteAllText((Join-Path $s.Target 'a.txt'), 'old')
    [IO.File]::WriteAllText((Join-Path $s.Payload 'a.txt'), 'new')
    Apply $s
    Assert ((Get-Content -LiteralPath (Join-Path $s.Target 'a.txt') -Raw) -eq 'new') 'New file not applied'
    Assert (!(Test-Path -LiteralPath (Join-Path $s.Updates 'pending.json'))) 'Pending marker not cleared'
    Write-Output 'PASS manual startup applies update and launches application'

    $s = Scenario 'locked'
    $lockedPath = Join-Path $s.Target 'a.txt'
    [IO.File]::WriteAllText($lockedPath, 'old')
    [IO.File]::WriteAllText((Join-Path $s.Payload 'a.txt'), 'new')
    $lock = [IO.File]::Open($lockedPath, 'Open', 'Read', 'None')
    try { Apply $s } finally { $lock.Dispose() }
    Assert ((Get-Content -LiteralPath $lockedPath -Raw) -eq 'old') 'Locked installation changed'
    Assert (Test-Path -LiteralPath (Join-Path $s.Updates 'pending.json')) 'Locked update lost its pending marker'
    Assert (Test-Path -LiteralPath (Join-Path $s.Updates 'apply-error.txt')) 'Lock failure not reported'
    Write-Output 'PASS locked installation remains intact and activation is postponed'

    $s = Scenario 'rollback'
    [IO.File]::WriteAllText((Join-Path $s.Target 'a.txt'), 'old')
    [IO.File]::WriteAllText((Join-Path $s.Payload 'a.txt'), 'new')
    [IO.File]::WriteAllText((Join-Path $s.Payload 'new.txt'), 'new file')
    [IO.File]::WriteAllText((Join-Path $s.Target 'blocked'), 'not a directory')
    [IO.Directory]::CreateDirectory((Join-Path $s.Payload 'blocked')) | Out-Null
    [IO.File]::WriteAllText((Join-Path $s.Payload 'blocked/b.txt'), 'fails after first copy')
    Apply $s
    Assert ((Get-Content -LiteralPath (Join-Path $s.Target 'a.txt') -Raw) -eq 'old') 'Rollback did not restore old file'
    Assert (!(Test-Path -LiteralPath (Join-Path $s.Target 'new.txt'))) 'Rollback left newly added file'
    Assert (Test-Path -LiteralPath (Join-Path $s.Updates 'pending.json')) 'Failed update lost its pending marker'
    Write-Output 'PASS copy failure rolls back changed and newly added files'
} finally {
    $resolved = [IO.Path]::GetFullPath($testRoot)
    if (!$resolved.StartsWith([IO.Path]::GetFullPath([IO.Path]::GetTempPath()), [StringComparison]::OrdinalIgnoreCase)) { throw 'Unsafe test cleanup path' }
    Remove-Item -LiteralPath $resolved -Recurse -Force
}
