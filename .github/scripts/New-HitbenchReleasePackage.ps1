param([Parameter(Mandatory)][string]$PublishDirectory, [Parameter(Mandatory)][string]$Version,
    [Parameter(Mandatory)][string]$Commit, [Parameter(Mandatory)][string]$DestinationZip)
$ErrorActionPreference = 'Stop'
$payloadRoot = [IO.Path]::GetFullPath($PublishDirectory)
if (-not (Test-Path -LiteralPath (Join-Path $payloadRoot 'Hitbench.exe'))) { throw 'Missing application executable.' }
foreach ($protected in @('data.json', 'projects', 'saved')) {
    if (Test-Path -LiteralPath (Join-Path $payloadRoot $protected)) { throw 'Publish output contains user data.' }
}
$settings = Join-Path $payloadRoot 'appsettings.json'
if (Test-Path -LiteralPath $settings) { Remove-Item -LiteralPath $settings }
$files = @(Get-ChildItem -LiteralPath $payloadRoot -File -Recurse | ForEach-Object {
    if (($_.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw 'Linked payload file.' }
    $_.FullName.Substring($payloadRoot.Length + 1).Replace('\', '/')
} | Where-Object { $_ -ne 'hitbench-release.json' }) + @('hitbench-release.json')
@{ Version = $Version; Commit = $Commit; Files = $files } | ConvertTo-Json -Depth 4 |
    Set-Content -LiteralPath (Join-Path $payloadRoot 'hitbench-release.json') -Encoding UTF8
Compress-Archive -Path (Join-Path $payloadRoot '*') -DestinationPath $DestinationZip
