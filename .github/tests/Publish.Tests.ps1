$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$parse = Join-Path $root 'scripts/Get-HitbenchBuildVersion.ps1'
foreach ($case in @(@('1.2.3','official'), @('1.2.3-beta','beta'), @('1.2.3beta','beta'), @('1.2.3-hotfix','hotfix'), @('1.2.3-BETA.2','beta'))) {
    $result = & $parse -Subject "Last commit for build $($case[0])"
    if ($result.Version -cne $case[0] -or $result.Description -cne $case[1] -or $result.NumericVersion -ne '1.2.3') { throw "Wrong parse: $case" }
}
if (& $parse -Subject 'Ordinary commit') { throw 'Ordinary commits must not publish.' }
foreach ($invalid in @('1.2','1.2.3-','1.2.3-beta..','1.2.3/evil','9999999999999.2.3','1.2.3.4','1.2.3-beta.lock')) {
    $rejected = $false
    try { & $parse -Subject "Last commit for build $invalid" } catch { $rejected = $true }
    if (-not $rejected) { throw "Accepted invalid version $invalid" }
}
$fixture = Join-Path ([IO.Path]::GetTempPath()) ('hitbench-publish-test-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $fixture | Out-Null
try {
    $remote = Join-Path $fixture 'remote.git'
    git init --bare $remote 2>&1 | Out-Null
    $source = Join-Path $fixture 'source'
    git init $source 2>&1 | Out-Null
    git -C $source -c user.name=Test -c user.email=test@example.invalid commit --allow-empty -m first | Out-Null
    $first = git -C $source rev-parse HEAD
    git -C $source -c user.name=Test -c user.email=test@example.invalid commit --allow-empty -m second | Out-Null
    $second = git -C $source rev-parse HEAD
    git -C $source push $remote "${first}:refs/tags/1.2.3" "${second}:refs/tags/1.2.4" 2>&1 | Out-Null
    Push-Location $source
    try {
        & (Join-Path $root 'scripts/Set-HitbenchDescriptionTag.ps1') -Description official -Commit $first -Remote $remote
        & (Join-Path $root 'scripts/Set-HitbenchDescriptionTag.ps1') -Description official -Commit $second -Remote $remote
        $actual = git ls-remote $remote refs/tags/official
        if (-not $actual.StartsWith($second)) { throw 'Channel did not advance.' }
        if (-not (git ls-remote $remote refs/tags/1.2.3).StartsWith($first)) { throw 'Permanent version tag changed.' }
        # Move the remote after lease capture but before the actual push reaches it.
        $hookPath = Join-Path $source '.git/hooks/pre-push'
        $remoteForHook = $remote.Replace('\', '/')
        [IO.File]::WriteAllText($hookPath, "#!/bin/sh`ngit --git-dir='$remoteForHook' update-ref refs/tags/official $first $second`n", [Text.UTF8Encoding]::new($false))
        $leaseRejected = $false
        try { & (Join-Path $root 'scripts/Set-HitbenchDescriptionTag.ps1') -Description official -Commit $first -Remote $remote }
        catch { $leaseRejected = $true }
        finally { Remove-Item -LiteralPath $hookPath }
        if (-not $leaseRejected) { throw 'Concurrent description tag change was overwritten.' }
    } finally { Pop-Location }
    $publish = Join-Path $fixture 'payload'
    New-Item -ItemType Directory -Path $publish | Out-Null
    [IO.File]::WriteAllText((Join-Path $publish 'Hitbench.exe'), 'fixture')
    [IO.File]::WriteAllText((Join-Path $publish 'appsettings.json'), 'user defaults')
    $zip = Join-Path $fixture 'fixture.zip'
    & (Join-Path $root 'scripts/New-HitbenchReleasePackage.ps1') -PublishDirectory $publish -Version '1.2.4' -Commit $second -DestinationZip $zip
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $archive = [IO.Compression.ZipFile]::OpenRead($zip)
    try {
        if ($archive.Entries.FullName -contains 'appsettings.json') { throw 'Settings included in update.' }
        if ($archive.Entries.FullName -notcontains 'hitbench-release.json') { throw 'Missing identity.' }
    } finally { $archive.Dispose() }
    $manifest = Get-Content -LiteralPath (Join-Path $publish 'hitbench-release.json') -Raw | ConvertFrom-Json
    if ($manifest.Version -ne '1.2.4' -or $manifest.Commit -ne $second -or $manifest.Files -notcontains 'Hitbench.exe') { throw 'Invalid package identity.' }
    New-Item -ItemType Directory -Path (Join-Path $publish 'saved') | Out-Null
    $protectedRejected = $false
    try { & (Join-Path $root 'scripts/New-HitbenchReleasePackage.ps1') -PublishDirectory $publish -Version '1.2.4' -Commit $second -DestinationZip (Join-Path $fixture 'invalid.zip') }
    catch { $protectedRejected = $true }
    if (-not $protectedRejected) { throw 'User data included in release package.' }
    Write-Output 'Publish checks passed.'
} finally {
    $full = [IO.Path]::GetFullPath($fixture)
    if (-not $full.StartsWith([IO.Path]::GetTempPath(), [StringComparison]::OrdinalIgnoreCase)) { throw 'Invalid fixture root.' }
    Remove-Item -LiteralPath $full -Recurse -Force
}
