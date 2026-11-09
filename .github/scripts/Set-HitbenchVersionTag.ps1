param([Parameter(Mandatory)][string]$Version, [Parameter(Mandatory)][string]$Commit, [string]$Remote = 'origin')
$ErrorActionPreference = 'Stop'
$build = & (Join-Path $PSScriptRoot 'Get-HitbenchBuildVersion.ps1') -Subject "Last commit for build $Version"
if (-not $build -or $build.Version -cne $Version -or $Commit -cnotmatch '\A[0-9a-f]{40,64}\z') { throw 'Invalid version tag or commit.' }
$ref = "refs/tags/$Version"
$existing = git ls-remote --refs $Remote $ref
if ($LASTEXITCODE -ne 0) { throw 'Unable to read version tag.' }
$expected = if ($existing) { ($existing -split '\s+')[0] } else { '' }
git push "--force-with-lease=${ref}:$expected" $Remote "${Commit}:$ref"
if ($LASTEXITCODE -ne 0) { throw 'Version tag changed concurrently or could not be pushed.' }
