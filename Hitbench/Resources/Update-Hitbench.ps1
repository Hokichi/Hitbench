function Assert-OHsUpdatePath {
    param([string]$Path)
    for ($current = [IO.Path]::GetFullPath($Path); $current; $current = [IO.Path]::GetDirectoryName($current)) {
        if (Test-Path -LiteralPath $current) {
            if (((Get-Item -LiteralPath $current -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw 'Linked update path.' }
        }
    }
}

function Resolve-OHsUpdateFile {
    param([string]$Root, [string]$Relative)
    if (-not $Relative -or [IO.Path]::IsPathRooted($Relative) -or $Relative.Contains('\') -or $Relative.Contains(':')) { throw 'Invalid update path.' }
    $parts = $Relative.Split('/')
    if (@('appsettings.json','data.json','projects','saved') -contains $parts[0]) { throw 'Update targets user data.' }
    foreach ($part in $parts) {
        if (-not $part -or $part -in @('.','..') -or $part.EndsWith('.') -or $part.EndsWith(' ') -or $part.IndexOfAny([IO.Path]::GetInvalidFileNameChars()) -ge 0 -or
            ($part.Split('.')[0] -match '^(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])$')) { throw 'Invalid update component.' }
    }
    $full = [IO.Path]::GetFullPath((Join-Path $Root $Relative))
    if (-not $full.StartsWith($Root.TrimEnd('\') + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Escaped update path.' }
    Assert-OHsUpdatePath $full
    return $full
}

function Invoke-OHsUpdate {
    param([Parameter(Mandatory)][string]$JobPath,
        [scriptblock]$WaitForExit = {
            param($job)
            $original = Get-Process -Id $job.OriginalProcessId -ErrorAction SilentlyContinue
            if (-not $original) { return $true }
            if ($original.StartTime.ToUniversalTime().ToString('O') -ne $job.OriginalProcessStartTime) { return $false }
            return $original.WaitForExit(60000)
        },
        [scriptblock]$StartApplication = { param($exe,$working) Start-Process -FilePath $exe -WorkingDirectory $working | Out-Null },
        [scriptblock]$CopyFile = { param($source,$destination) Copy-Item -LiteralPath $source -Destination $destination -Force })
    $ErrorActionPreference = 'Stop'
    $job = Get-Content -LiteralPath $JobPath -Raw | ConvertFrom-Json
    $install = [IO.Path]::GetFullPath($job.InstallDirectory).TrimEnd('\')
    $workspace = [IO.Path]::GetFullPath($job.Workspace).TrimEnd('\')
    $payload = [IO.Path]::GetFullPath($job.PayloadDirectory).TrimEnd('\')
    Assert-OHsUpdatePath $install
    Assert-OHsUpdatePath $workspace
    if ($install -eq [IO.Path]::GetPathRoot($install).TrimEnd('\') -or
        $workspace.StartsWith($install + '\', [StringComparison]::OrdinalIgnoreCase) -or $workspace -eq $install -or
        $payload -ne (Join-Path $workspace 'payload') -or [IO.Path]::GetFullPath($JobPath) -ne (Join-Path $workspace 'job.json') -or
        $job.Executable -cne 'Hitbench.exe' -or -not (Test-Path -LiteralPath (Join-Path $install 'Hitbench.exe')) -or
        @($job.Files).Count -eq 0 -or @($job.Files).Count -gt 10000) { throw 'Invalid update job roots.' }
    $seen = @{}
    foreach ($file in $job.Files) {
        if ($seen.ContainsKey($file)) { throw 'Duplicate payload file.' }
        $seen[$file] = $true
        $source = Resolve-OHsUpdateFile $payload $file
        $null = Resolve-OHsUpdateFile $install $file
        if (-not [IO.File]::Exists($source)) { throw 'Missing staged file.' }
    }
    if (-not $seen.ContainsKey('Hitbench.exe')) { throw 'Missing executable.' }
    # Probe write access before acknowledging readiness; no installed file is changed.
    $probe = Join-Path $install ('.hitbench-update-' + [Guid]::NewGuid().ToString('N'))
    [IO.File]::WriteAllText($probe, '')
    Remove-Item -LiteralPath $probe
    $ready = Join-Path $workspace 'ready'
    $commit = Join-Path $workspace 'commit'
    $abort = Join-Path $workspace 'abort'
    [IO.File]::WriteAllText($ready, '')
    $watch = [Diagnostics.Stopwatch]::StartNew()
    while (-not (Test-Path -LiteralPath $commit)) {
        if ((Test-Path -LiteralPath $abort) -or $watch.Elapsed.TotalSeconds -gt 120) { return [pscustomobject]@{ Status='Aborted' } }
        Start-Sleep -Milliseconds 100
    }
    if ((Test-Path -LiteralPath $abort) -or -not (& $WaitForExit $job)) { return [pscustomobject]@{ Status='Aborted' } }
    $backup = Join-Path $workspace 'backup'
    New-Item -ItemType Directory -Path $backup -Force | Out-Null
    $originals = @()
    $changed = @()
    try {
        # Complete all backups before overwriting any application file.
        foreach ($file in $job.Files) {
            $destination = Resolve-OHsUpdateFile $install $file
            if ([IO.File]::Exists($destination)) {
                $saved = Resolve-OHsUpdateFile $backup $file
                New-Item -ItemType Directory -Path ([IO.Path]::GetDirectoryName($saved)) -Force | Out-Null
                Copy-Item -LiteralPath $destination -Destination $saved -Force
                $originals += $file
            }
        }
        foreach ($file in $job.Files) {
            $source = Resolve-OHsUpdateFile $payload $file
            $destination = Resolve-OHsUpdateFile $install $file
            New-Item -ItemType Directory -Path ([IO.Path]::GetDirectoryName($destination)) -Force | Out-Null
            $changed += $file
            & $CopyFile $source $destination
        }
    } catch {
        $failure = $_
        try {
            [array]::Reverse($changed)
            foreach ($file in $changed) {
                $destination = Resolve-OHsUpdateFile $install $file
                if ($originals -contains $file) { Copy-Item -LiteralPath (Resolve-OHsUpdateFile $backup $file) -Destination $destination -Force }
                elseif ([IO.File]::Exists($destination)) { Remove-Item -LiteralPath $destination }
            }
            & $StartApplication (Join-Path $install 'Hitbench.exe') $install
            return [pscustomobject]@{ Status='RolledBack' }
        } catch {
            [IO.File]::WriteAllText((Join-Path $workspace 'update-error.log'), "$failure`r`n$_")
            return [pscustomobject]@{ Status='RecoveryRequired' }
        }
    }
    try { & $StartApplication (Join-Path $install 'Hitbench.exe') $install }
    catch {
        [IO.File]::WriteAllText((Join-Path $workspace 'update-error.log'), [string]$_)
        return [pscustomobject]@{ Status='LaunchFailed' }
    }
    Assert-OHsUpdatePath $workspace
    Remove-Item -LiteralPath $workspace -Recurse -Force
    return [pscustomobject]@{ Status='Installed' }
}
