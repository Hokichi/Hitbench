$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$parse = Join-Path $root 'scripts/Get-HitbenchBuildVersion.ps1'
function Assert-Fails([scriptblock]$Action, [string]$Message) {
    $rejected = $false
    try { & $Action } catch { $rejected = $true }
    if (-not $rejected) { throw $Message }
}
foreach ($version in @('1.2.3', '1.2.3-beta', '1.2.3beta', '1.2.3-hotfix', '1.2.3-BETA.2')) {
    $result = & $parse -Subject "Last commit for build $version"
    if ($result.Version -cne $version -or $result.NumericVersion -ne '1.2.3') { throw "Wrong parse: $version" }
    if ($result.PSObject.Properties.Name -contains 'Description') { throw 'Build parser still emits a separate description.' }
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
        $setTag = Join-Path $root 'scripts/Set-HitbenchVersionTag.ps1'
        $versionRef = 'refs/tags/1.2.3-hotfix'
        & $setTag -Version '1.2.3-hotfix' -Commit $first -Remote $remote
        if (-not (git ls-remote $remote $versionRef).StartsWith($first)) { throw 'Version tag was not created.' }
        & $setTag -Version '1.2.3-hotfix' -Commit $second -Remote $remote
        if (-not (git ls-remote $remote $versionRef).StartsWith($second)) { throw 'Version tag did not advance.' }
        & $setTag -Version '1.2.3-hotfix' -Commit $second -Remote $remote
        if (-not (git ls-remote $remote $versionRef).StartsWith($second)) { throw 'Identical tag retry failed.' }
        if (-not (git ls-remote $remote refs/tags/1.2.3).StartsWith($first)) { throw 'Unrelated version tag changed.' }
        if (git ls-remote $remote refs/tags/hotfix refs/tags/official) { throw 'Separate description tag was created.' }
        foreach ($invalid in @('', '1.2', '1.2.3/evil')) {
            Assert-Fails { & $setTag -Version $invalid -Commit $first -Remote $remote } 'Invalid version was accepted.'
        }
        Assert-Fails { & $setTag -Version '1.2.3-hotfix' -Commit 'invalid' -Remote $remote } 'Invalid commit was accepted.'
        Assert-Fails { & $setTag -Version '1.2.3-hotfix' -Commit $first -Remote (Join-Path $fixture 'missing.git') } 'Remote read failure was ignored.'
        # Move the remote after lease capture but before the actual push reaches it.
        $hookPath = Join-Path $source '.git/hooks/pre-push'
        $remoteForHook = $remote.Replace('\', '/')
        [IO.File]::WriteAllText($hookPath, "#!/bin/sh`ngit --git-dir='$remoteForHook' update-ref $versionRef $first $second`n", [Text.UTF8Encoding]::new($false))
        try { Assert-Fails { & $setTag -Version '1.2.3-hotfix' -Commit $first -Remote $remote } 'Concurrent version tag change was overwritten.' }
        finally { Remove-Item -LiteralPath $hookPath }
        if (-not (git ls-remote $remote $versionRef).StartsWith($first)) { throw 'Competing tag update was not preserved.' }

        $publisher = Join-Path $root 'scripts/Publish-HitbenchRelease.ps1'
        $releaseArchive = Join-Path $fixture 'Hitbench-1.2.3-hotfix-win-x64.zip'
        [IO.File]::WriteAllText($releaseArchive, 'release fixture')
        $repositoryBefore = $env:GITHUB_REPOSITORY
        $exitCodeBefore = $LASTEXITCODE
        $ghState = @{ Calls = [Collections.Generic.List[object]]::new(); ReleaseId = ''; Failure = '' }
        function gh {
            $ghState.Calls.Add([string[]]$args)
            $operation = if ($args[0] -eq 'api') { 'lookup' } else { $args[1] }
            $global:LASTEXITCODE = if ($ghState.Failure -eq $operation) { 1 } else { 0 }
            if ($operation -eq 'lookup' -and $LASTEXITCODE -eq 0) { $ghState.ReleaseId }
        }
        function Assert-GhCalls([object[]]$Expected) {
            if ((ConvertTo-Json -InputObject $ghState.Calls.ToArray() -Depth 4 -Compress) -cne
                (ConvertTo-Json -InputObject $Expected -Depth 4 -Compress)) { throw 'Unexpected GitHub CLI operations or arguments.' }
        }
        try {
            $env:GITHUB_REPOSITORY = 'test/Hitbench'
            $lookup = @('api', 'repos/test/Hitbench/releases', '--paginate', '--jq', '.[] | select(.tag_name == "1.2.3-hotfix") | .id')
            $create = @('release', 'create', '1.2.3-hotfix', $releaseArchive, '--verify-tag', '--target', $second, '--title', '1.2.3-hotfix', '--generate-notes')
            # A deleted release leaves a tag behind; publishing must advance that tag and create the release.
            & $publisher -Version '1.2.3-hotfix' -Commit $second -Archive $releaseArchive -Remote $remote
            Assert-GhCalls @($lookup, $create)
            if (-not (git ls-remote $remote $versionRef).StartsWith($second)) { throw 'Recreated release retained the old tag commit.' }

            $ghState.Calls.Clear()
            & $publisher -Version '1.2.3-BETA.2' -Commit $second -Archive $releaseArchive -Remote $remote
            if (-not (git ls-remote $remote refs/tags/1.2.3-BETA.2).StartsWith($second)) { throw 'Release with missing tag was not created.' }
            if ($ghState.Calls.Count -ne 2 -or $ghState.Calls[1][1] -ne 'create' -or $ghState.Calls[1][2] -cne '1.2.3-BETA.2') { throw 'Full release suffix was not preserved.' }

            $ghState.Calls.Clear()
            $ghState.ReleaseId = '42'
            & $publisher -Version '1.2.3-hotfix' -Commit $second -Archive $releaseArchive -Remote $remote
            Assert-GhCalls @($lookup,
                @('release', 'upload', '1.2.3-hotfix', $releaseArchive, '--clobber'),
                @('release', 'edit', '1.2.3-hotfix', '--target', $second, '--draft=false'))

            $ghState.Calls.Clear()
            Assert-Fails { & $publisher -Version '1.2.3-hotfix' -Commit $first -Archive (Join-Path $fixture 'missing.zip') -Remote $remote } 'Missing archive was accepted.'
            Assert-GhCalls @()
            if (-not (git ls-remote $remote $versionRef).StartsWith($second)) { throw 'Missing archive changed the tag.' }
            foreach ($invalid in @('', '1.2.3/evil', "1.2.3`nignored")) {
                Assert-Fails { & $publisher -Version $invalid -Commit $first -Archive $releaseArchive -Remote $remote } 'Invalid publisher version was accepted.'
            }
            Assert-Fails { & $publisher -Version '1.2.3-hotfix' -Commit 'invalid' -Archive $releaseArchive -Remote $remote } 'Invalid publisher commit was accepted.'
            Assert-GhCalls @()

            $ghState.Failure = 'lookup'
            Assert-Fails { & $publisher -Version '1.2.3-hotfix' -Commit $first -Archive $releaseArchive -Remote $remote } 'Release lookup failure was ignored.'
            Assert-GhCalls @(,$lookup)
            if (-not (git ls-remote $remote $versionRef).StartsWith($second)) { throw 'Failed lookup changed the tag.' }

            $ghState.Calls.Clear()
            $ghState.Failure = ''
            [IO.File]::WriteAllText($hookPath, "#!/bin/sh`nexit 1`n", [Text.UTF8Encoding]::new($false))
            try { Assert-Fails { & $publisher -Version '1.2.3-hotfix' -Commit $first -Archive $releaseArchive -Remote $remote } 'Rejected tag push was ignored.' }
            finally { Remove-Item -LiteralPath $hookPath }
            Assert-GhCalls @(,$lookup)
            if (-not (git ls-remote $remote $versionRef).StartsWith($second)) { throw 'Rejected push changed the tag.' }

            foreach ($failure in @('create', 'upload', 'edit')) {
                $ghState.Calls.Clear()
                $ghState.Failure = $failure
                $ghState.ReleaseId = if ($failure -eq 'create') { '' } else { '42' }
                Assert-Fails { & $publisher -Version '1.2.3-hotfix' -Commit $second -Archive $releaseArchive -Remote $remote } "Failed release $failure was ignored."
                $expectedCount = if ($failure -eq 'edit') { 3 } else { 2 }
                if ($ghState.Calls.Count -ne $expectedCount -or $ghState.Calls[$expectedCount - 1][1] -ne $failure) { throw "Release mutation continued after failed $failure." }
            }
        } finally {
            Remove-Item Function:gh
            Remove-Item Function:Assert-GhCalls
            $env:GITHUB_REPOSITORY = $repositoryBefore
            $global:LASTEXITCODE = $exitCodeBefore
        }
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
