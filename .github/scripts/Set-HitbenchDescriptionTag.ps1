param([Parameter(Mandatory)][string]$Description, [Parameter(Mandatory)][string]$Commit, [string]$Remote = 'origin')
$ErrorActionPreference = 'Stop'
if ($Description -cnotmatch '\A[a-z][a-z0-9]*\z' -or $Commit -cnotmatch '\A[0-9a-f]{40,64}\z') { throw 'Invalid description tag or commit.' }
$ref = "refs/tags/$Description"
$existing = git ls-remote --refs $Remote $ref
if ($LASTEXITCODE -ne 0) { throw 'Unable to read description tag.' }
$expected = if ($existing) { ($existing -split '\s+')[0] } else { '' }
git push "--force-with-lease=${ref}:$expected" $Remote "${Commit}:$ref"
if ($LASTEXITCODE -ne 0) { throw 'Description tag changed concurrently or could not be pushed.' }
