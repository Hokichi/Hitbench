$ErrorActionPreference = 'Stop'
$rates = 8000, 11025, 12000, 16000, 22050, 24000, 32000, 44100, 48000, 88200, 96000
$manifest = @()
foreach ($rate in $rates) {
    foreach ($role in 'song0', 'song100', 'sample0', 'sample100') {
        $isSong = $role.StartsWith('song')
        $padding = if ($role.EndsWith('100')) { 0.1 } else { 0 }
        $frame = [int][Math]::Round(($padding + [int]$isSong) * $rate, [MidpointRounding]::AwayFromZero)
        $frames = [int][Math]::Ceiling($(if ($isSong) { 1.4 } else { 0.3 }) * $rate)
        $channel = if ($isSong) { 0 } else { 1 }
        $pcm = New-Object byte[] ($frames * 8)
        [BitConverter]::GetBytes([single]0.5).CopyTo($pcm, ($frame * 2 + $channel) * 4)
        $stem = "timing-$rate-$role"
        $wavPath = Join-Path $PSScriptRoot "$stem.wav"
        $writer = [IO.BinaryWriter]::new([IO.File]::Create($wavPath))
        try {
            $writer.Write([Text.Encoding]::ASCII.GetBytes('RIFF'))
            $writer.Write([int](36 + $pcm.Length))
            $writer.Write([Text.Encoding]::ASCII.GetBytes('WAVEfmt '))
            $writer.Write([int]16)
            $writer.Write([int16]3)
            $writer.Write([int16]2)
            $writer.Write([int]$rate)
            $writer.Write([int]($rate * 8))
            $writer.Write([int16]8)
            $writer.Write([int16]32)
            $writer.Write([Text.Encoding]::ASCII.GetBytes('data'))
            $writer.Write([int]$pcm.Length)
            $writer.Write($pcm)
        } finally { $writer.Dispose() }
        $outputs = @("$stem.wav", "$stem.ogg")
        & ffmpeg -hide_banner -loglevel error -y -i $wavPath -c:a libvorbis -q:a 4 (Join-Path $PSScriptRoot "$stem.ogg")
        if ($LASTEXITCODE -ne 0) { throw "Vorbis encoding failed: $stem" }
        if ($rate -le 48000) {
            foreach ($xing in 1, 0) {
                $name = if ($xing) { "$stem.mp3" } else { "$stem-no-xing.mp3" }
                & ffmpeg -hide_banner -loglevel error -y -i $wavPath -c:a libmp3lame -q:a 2 -write_xing $xing (Join-Path $PSScriptRoot $name)
                if ($LASTEXITCODE -ne 0) { throw "MP3 encoding failed: $name" }
                $outputs += $name
            }
        }
        foreach ($name in $outputs) {
            $actualRate = & ffprobe -v error -select_streams a:0 -show_entries stream=sample_rate -of default=noprint_wrappers=1:nokey=1 (Join-Path $PSScriptRoot $name)
            if ($LASTEXITCODE -ne 0 -or [int]$actualRate -ne $rate) { throw "Unexpected sample rate: $name ($actualRate)" }
            $manifest += [ordered]@{ File = $name; Rate = $rate; Frame = $frame; Channel = $channel; Role = $role }
        }
    }
}
$manifest | ConvertTo-Json | Set-Content (Join-Path $PSScriptRoot 'timing-manifest.json') -Encoding utf8
(& ffmpeg -version | Select-Object -First 1) | Set-Content (Join-Path $PSScriptRoot 'timing-encoder.txt')
Write-Output "Generated and verified $($manifest.Count) fixtures."
