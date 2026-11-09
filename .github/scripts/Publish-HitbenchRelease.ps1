param([Parameter(Mandatory)][string]$Version, [Parameter(Mandatory)][string]$Commit,
    [Parameter(Mandatory)][string]$Archive, [string]$Remote = 'origin')
$ErrorActionPreference = 'Stop'
if (-not (Test-Path -LiteralPath $Archive -PathType Leaf)) { throw 'Missing release archive.' }
$build = & (Join-Path $PSScriptRoot 'Get-HitbenchBuildVersion.ps1') -Subject "Last commit for build $Version"
if (-not $build -or $build.Version -cne $Version -or $Commit -cnotmatch '\A[0-9a-f]{40,64}\z') { throw 'Invalid version tag or commit.' }
if ([string]::IsNullOrWhiteSpace($env:GITHUB_REPOSITORY)) { throw 'Missing GitHub repository.' }
$release = gh api "repos/$env:GITHUB_REPOSITORY/releases" --paginate --jq ".[] | select(.tag_name == `"$Version`") | .id"
if ($LASTEXITCODE -ne 0) { throw 'Unable to check existing releases.' }

& (Join-Path $PSScriptRoot 'Set-HitbenchVersionTag.ps1') -Version $Version -Commit $Commit -Remote $Remote
if ($release) {
    gh release upload $Version $Archive --clobber
    if ($LASTEXITCODE -ne 0) { throw 'Unable to replace release archive.' }
    gh release edit $Version --target $Commit --draft=false
    if ($LASTEXITCODE -ne 0) { throw 'Unable to publish updated GitHub Release.' }
} else {
    gh release create $Version $Archive --verify-tag --target $Commit --title $Version --generate-notes
    if ($LASTEXITCODE -ne 0) { throw 'Unable to create GitHub Release.' }
}
