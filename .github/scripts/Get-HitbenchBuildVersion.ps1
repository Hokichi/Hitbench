param([Parameter(Mandatory)][string]$Subject)
$ErrorActionPreference = 'Stop'
$subjectLine = ($Subject -split '\r?\n', 2)[0]
$prefix = 'Last commit for build '
if (-not $subjectLine.StartsWith($prefix, [StringComparison]::Ordinal)) { return }
$version = $subjectLine.Substring($prefix.Length)
if ($version.Length -gt 128 -or $version.Contains('..') -or $version.EndsWith('.lock', [StringComparison]::OrdinalIgnoreCase) -or
    $version -cnotmatch '\A(\d+)\.(\d+)\.(\d+)(?:[-._]?([A-Za-z][A-Za-z0-9]*)(?:[._-][A-Za-z0-9]+)*)?\z') { throw 'Invalid release version.' }
$parts = @($Matches[1], $Matches[2], $Matches[3])
$numeric = foreach ($part in $parts) { $parsed = 0; if (-not [int]::TryParse($part, [ref]$parsed)) { throw 'Version component is too large.' }; $parsed }
git check-ref-format "refs/tags/$version"
if ($LASTEXITCODE -ne 0) { throw 'Invalid version tag.' }
[pscustomobject]@{ Version = $version; NumericVersion = ($numeric -join '.') }
