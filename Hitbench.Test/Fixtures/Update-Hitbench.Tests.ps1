$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot '../../Hitbench/Resources/Update-Hitbench.ps1')
$root = Join-Path ([IO.Path]::GetTempPath()) ('hitbench-helper-test-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $root | Out-Null
try {
    foreach ($fail in @($false, $true)) {
        $caseRoot = Join-Path $root ([Guid]::NewGuid().ToString('N'))
        $install = Join-Path $caseRoot "app's folder Thai"
        $workspace = Join-Path $caseRoot 'workspace'
        $payload = Join-Path $workspace 'payload'
        New-Item -ItemType Directory -Path $install,$payload -Force | Out-Null
        [IO.File]::WriteAllText((Join-Path $install 'Hitbench.exe'), 'old')
        [IO.File]::WriteAllText((Join-Path $install 'appsettings.json'), 'settings')
        [IO.File]::WriteAllText((Join-Path $install 'unrelated.txt'), 'keep')
        New-Item -ItemType Directory -Path (Join-Path $install 'projects'),(Join-Path $install 'saved') | Out-Null
        [IO.File]::WriteAllText((Join-Path $install 'saved/sample.wav'), 'sample')
        [IO.File]::WriteAllText((Join-Path $payload 'Hitbench.exe'), 'new')
        [IO.File]::WriteAllText((Join-Path $payload 'new.dll'), 'new dependency')
        $job = @{ InstallDirectory=$install; Workspace=$workspace; PayloadDirectory=$payload; Files=@('Hitbench.exe','new.dll'); OriginalProcessId=123; OriginalProcessStartTime='2026-11-03T00:00:00Z'; Executable='Hitbench.exe' }
        $jobPath = Join-Path $workspace 'job.json'
        $job | ConvertTo-Json | Set-Content -LiteralPath $jobPath -Encoding UTF8
        [IO.File]::WriteAllText((Join-Path $workspace 'commit'), '')
        $script:started = $null
        $script:copies = 0
        $script:failCopy = $fail
        $result = Invoke-OHsUpdate -JobPath $jobPath -WaitForExit { param($j) $true } -StartApplication { param($exe,$working) $script:started = $exe } -CopyFile {
            param($source,$destination)
            $script:copies++
            if ($script:failCopy -and $script:copies -eq 2) { throw 'Injected copy failure.' }
            Copy-Item -LiteralPath $source -Destination $destination -Force
        }
        $expected = if ($fail) { 'old' } else { 'new' }
        if ([IO.File]::ReadAllText((Join-Path $install 'Hitbench.exe')) -ne $expected) { throw 'Wrong installed executable after replacement/rollback.' }
        if ($fail -and (Test-Path -LiteralPath (Join-Path $install 'new.dll'))) { throw 'Rollback left new file.' }
        if ([IO.File]::ReadAllText((Join-Path $install 'appsettings.json')) -ne 'settings' -or [IO.File]::ReadAllText((Join-Path $install 'saved/sample.wav')) -ne 'sample') { throw 'User data changed.' }
        if ($script:started -ne (Join-Path $install 'Hitbench.exe')) { throw 'Did not restart correct application.' }
    }
    # Reuse the retained rollback fixture to force restoration itself to fail.
    $script:started = $null
    $script:copies = 0
    $script:locked = $null
    try {
        $recovery = Invoke-OHsUpdate -JobPath $jobPath -WaitForExit { param($j) $true } -StartApplication { param($exe,$working) $script:started = $exe } -CopyFile {
            param($source,$destination)
            $script:copies++
            if ($script:copies -eq 2) {
                $script:locked = [IO.File]::Open((Join-Path $install 'Hitbench.exe'), [IO.FileMode]::Open, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None)
                throw 'Injected copy failure with locked rollback destination.'
            }
            Copy-Item -LiteralPath $source -Destination $destination -Force
        }
        if ($recovery.Status -ne 'RecoveryRequired' -or $script:started -or -not (Test-Path -LiteralPath (Join-Path $workspace 'backup/Hitbench.exe'))) { throw 'Failed rollback launched mixed application or lost recovery data.' }
    } finally { if ($script:locked) { $script:locked.Dispose() } }
    Remove-Item -LiteralPath (Join-Path $workspace 'commit')
    [IO.File]::WriteAllText((Join-Path $workspace 'abort'), '')
    $aborted = Invoke-OHsUpdate -JobPath $jobPath -WaitForExit { throw 'Aborted job waited for exit.' } -StartApplication { throw 'Aborted job restarted app.' }
    if ($aborted.Status -ne 'Aborted') { throw 'Abort did not prevent replacement.' }
    Remove-Item -LiteralPath (Join-Path $workspace 'abort')
    [IO.File]::WriteAllText((Join-Path $workspace 'commit'), '')
    $job.OriginalProcessId = $PID
    $job.OriginalProcessStartTime = '1900-01-01T00:00:00Z'
    $job | ConvertTo-Json | Set-Content -LiteralPath $jobPath -Encoding UTF8
    $mismatch = Invoke-OHsUpdate -JobPath $jobPath -StartApplication { throw 'Wrong process identity restarted app.' }
    if ($mismatch.Status -ne 'Aborted') { throw 'Wrong process identity accepted.' }
    $job.Files = @('../escape')
    $job | ConvertTo-Json | Set-Content -LiteralPath $jobPath -Encoding UTF8
    $rejected = $false
    try { Invoke-OHsUpdate -JobPath $jobPath -StartApplication { throw 'Unsafe job restarted app.' } } catch { $rejected = $true }
    if (-not $rejected) { throw 'Helper accepted traversal.' }
    Write-Output 'Helper replacement, rollback, recovery, abort, identity and containment checks passed.'
} finally {
    $full = [IO.Path]::GetFullPath($root)
    if (-not $full.StartsWith([IO.Path]::GetTempPath(), [StringComparison]::OrdinalIgnoreCase)) { throw 'Unsafe fixture cleanup.' }
    Remove-Item -LiteralPath $full -Recurse -Force
}
