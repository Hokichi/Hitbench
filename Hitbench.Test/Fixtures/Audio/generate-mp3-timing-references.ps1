param([string]$BassDllPath = 'G:\osu!\bass.dll', [string]$OutputDirectory = $PSScriptRoot, [string]$RawDirectory = (Join-Path $env:TEMP 'osuhitsounder-mp3-reference'), [switch]$ConvertOnly)
$ErrorActionPreference = 'Stop'
if ($ConvertOnly) {
 $base = (Resolve-Path (Join-Path $PSScriptRoot '../../../osuHitsounder/bin/Debug/net10.0-windows')).Path
 [void][Reflection.Assembly]::LoadFrom((Join-Path $base 'NAudio.Core.dll'))
 $manifestPath = Join-Path $OutputDirectory 'mp3-bass-timing-reference.json'
 $manifest = Get-Content -Encoding UTF8 -LiteralPath $manifestPath -Raw | ConvertFrom-Json
 foreach ($case in $manifest.Cases) {
  $source = [NAudio.Wave.RawSourceWaveStream]::new([IO.File]::OpenRead((Join-Path $RawDirectory ($case.File+'.f32'))),[NAudio.Wave.WaveFormat]::CreateIeeeFloatWaveFormat($case.Rate,$case.Channels))
  try {
   $provider = [NAudio.Wave.SampleProviders.SampleChannel]::new($source,$false)
   if ($case.Channels -eq 1) {$provider=[NAudio.Wave.SampleProviders.MonoToStereoSampleProvider]::new($provider)}
   if ($case.Rate -ne 44100) {$provider=[NAudio.Wave.SampleProviders.WdlResamplingSampleProvider]::new($provider,44100)}
   $pcm=[IO.MemoryStream]::new();$buffer=[float[]]::new(8192);$bytes=[byte[]]::new(32768);$frames=0;$peak=0;$max=0
   while (($n=$provider.Read($buffer,0,8192)) -gt 0) {
    [Buffer]::BlockCopy($buffer,0,$bytes,0,$n*4);$pcm.Write($bytes,0,$n*4)
    for($i=$case.SignalChannel;$i -lt $n;$i+=2){$v=[Math]::Abs($buffer[$i]);if($v -gt $max){$max=$v;$peak=$frames+[Math]::Floor($i/2)}}
    $frames+=$n/2
   }
   $start=[Math]::Max(0,$peak-256);$count=[Math]::Min(1024,$frames-$start);$window=[byte[]]::new($count*8)
   [Buffer]::BlockCopy($pcm.ToArray(),$start*8,$window,0,$window.Length);$pcm.Dispose()
   $name=$case.File+'.converted-window.f32';[IO.File]::WriteAllBytes((Join-Path $OutputDirectory $name),$window)
   $case | Add-Member -Force -NotePropertyName ConvertedWindowSha256 -NotePropertyValue (Get-FileHash -LiteralPath (Join-Path $OutputDirectory $name) -Algorithm SHA256).Hash
   $case | Add-Member -Force -NotePropertyName ConvertedWindow -NotePropertyValue $name
   $case | Add-Member -Force -NotePropertyName ConvertedWindowStart -NotePropertyValue $start
   $case | Add-Member -Force -NotePropertyName ConvertedFrames -NotePropertyValue $frames
   $case | Add-Member -Force -NotePropertyName ConvertedPeakFrame -NotePropertyValue $peak
  } finally {$source.Dispose()}
 }
 $manifest | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $manifestPath -Encoding UTF8
 return
}
if ([IntPtr]::Size -ne 4) { throw 'Run with 32-bit Windows PowerShell for the installed BASS DLL.' }
# Generate only this compatibility corpus; original timing-* assets stay untouched.
foreach ($rate in 8000,22050,48000) {
 $original=Join-Path $OutputDirectory "timing-$rate-song100.wav"
 $destination=Join-Path $OutputDirectory "timing-$rate-song100-compat-mono.mp3"
 if (!(Test-Path -LiteralPath $destination)) {
  if ($rate -eq 22050) { & ffmpeg -v error -y -i $original -ac 1 -c:a libmp3lame -q:a 2 $destination }
  else { $bitrate=if($rate -eq 8000){'24k'}else{'128k'}; & ffmpeg -v error -y -i $original -ac 1 -c:a libmp3lame -b:a $bitrate $destination }
  if ($LASTEXITCODE -ne 0) {throw "Cannot generate $destination"}
 }
}
foreach ($variant in 'delay288','unknown-encoder','absent-delay') {
 $destination=Join-Path $OutputDirectory "timing-48000-song100-compat-$variant.mp3"
 if (!(Test-Path -LiteralPath $destination)) {
  $data=[IO.File]::ReadAllBytes((Join-Path $OutputDirectory 'timing-48000-song100.mp3'))
  $xing=[Text.Encoding]::ASCII.GetString($data).IndexOf('Xing')
  if ($xing -lt 0) {throw 'Synthetic source has no Xing header'}
  $flags=[Net.IPAddress]::NetworkToHostOrder([BitConverter]::ToInt32($data,$xing+4));$tag=$xing+8
  if($flags -band 1){$tag+=4};if($flags -band 2){$tag+=4};if($flags -band 4){$tag+=100};if($flags -band 8){$tag+=4}
  if($variant -eq 'unknown-encoder'){[Text.Encoding]::ASCII.GetBytes('Unknown00').CopyTo($data,$tag)}
  elseif($variant -eq 'absent-delay'){[Array]::Clear($data,$tag,36)}
  else {
   $oldDelay=($data[$tag+21] -shl 4) -bor ($data[$tag+22] -shr 4)
   $padding=(($data[$tag+22] -band 15) -shl 8) -bor $data[$tag+23]
   $padding += $oldDelay-288
   $data[$tag+21]=18;$data[$tag+22]=$padding -shr 8;$data[$tag+23]=$padding -band 255
  }
  [IO.File]::WriteAllBytes($destination,$data)
 }
}
$ProgressPreference = 'SilentlyContinue'
[void][IO.Directory]::CreateDirectory($RawDirectory)
Add-Type -TypeDefinition @"
using System; using System.Runtime.InteropServices;
public static class BassReference {
[DllImport(@"$BassDllPath")] public static extern uint BASS_GetVersion();
[DllImport(@"$BassDllPath")] public static extern bool BASS_Init(int d,uint r,uint f,IntPtr w,IntPtr c);
[DllImport(@"$BassDllPath")] public static extern bool BASS_SetConfig(uint o,uint v);
[DllImport(@"$BassDllPath",CharSet=CharSet.Unicode)] public static extern uint BASS_StreamCreateFile(bool m,string p,ulong o,ulong l,uint f);
[DllImport(@"$BassDllPath")] public static extern int BASS_ChannelGetData(uint h,float[] b,uint l);
[DllImport(@"$BassDllPath")] public static extern bool BASS_StreamFree(uint h);
[DllImport(@"$BassDllPath")] public static extern bool BASS_Free();
[DllImport(@"$BassDllPath")] public static extern bool BASS_ChannelGetInfo(uint h,IntPtr p);
}
"@
if ([BassReference]::BASS_GetVersion() -ne 0x2040F02) { throw 'Expected BASS 2.4.15.2.' }
[void][BassReference]::BASS_Init(0,44100,0,[IntPtr]::Zero,[IntPtr]::Zero)
[void][BassReference]::BASS_SetConfig(68,1)
$cases = @()
try {
 foreach ($file in Get-ChildItem -LiteralPath $OutputDirectory -Filter 'timing-*.mp3') {
  $rate = [int]$file.Name.Split('-')[1]
  $channel = if ($file.Name -match '-song') {0} else {1}
  $s = [BassReference]::BASS_StreamCreateFile($false,$file.FullName,0,0,2149712128)
  if (!$s) { throw "Cannot decode $($file.Name)" }
  try {
   $info = [Runtime.InteropServices.Marshal]::AllocHGlobal(40)
   try {
    if (![BassReference]::BASS_ChannelGetInfo($s,$info)) {throw 'Cannot read channel info'}
    $channels = [Runtime.InteropServices.Marshal]::ReadInt32($info,4)
    if ([Runtime.InteropServices.Marshal]::ReadInt32($info,0) -ne $rate) {throw "Unexpected native sample rate"}
   } finally {[Runtime.InteropServices.Marshal]::FreeHGlobal($info)}
   if ($channels -eq 1) {$channel=0}
   $pcm = [IO.MemoryStream]::new()
   $buffer = New-Object single[] 8192; $frames = 0; $peak = -1; $onset = -1; $maximum = 0
   while (($bytes = [BassReference]::BASS_ChannelGetData($s,$buffer,32768)) -gt 0) {
    $count = $bytes / (4*$channels)
    $raw = New-Object byte[] $bytes
    [Buffer]::BlockCopy($buffer,0,$raw,0,$bytes)
    $pcm.Write($raw,0,$bytes)
    for ($i=0; $i -lt $count; $i++) {
     $value = [Math]::Abs($buffer[$i*$channels+$channel])
     if ($value -gt $maximum) {$maximum=$value; $peak=$frames+$i}
     if ($onset -lt 0 -and $value -gt 0.0001) {$onset=$frames+$i}
    }
    $frames += $count
   }
   $all = $pcm.ToArray(); $pcm.Dispose()
   [IO.File]::WriteAllBytes((Join-Path $RawDirectory ($file.Name+'.f32')),$all)
   $windowStart = [Math]::Max(0,$peak-256)
   $windowFrames = [Math]::Min(1024,$frames-$windowStart)
   $windowBytes = New-Object byte[] ($windowFrames*$channels*4)
   [Buffer]::BlockCopy($all,$windowStart*$channels*4,$windowBytes,0,$windowBytes.Length)
   $windowName = $file.Name+'.bass-window.f32'
   [IO.File]::WriteAllBytes((Join-Path $OutputDirectory $windowName),$windowBytes)
   $cases += [ordered]@{NativeWindowSha256=(Get-FileHash -LiteralPath (Join-Path $OutputDirectory $windowName) -Algorithm SHA256).Hash;NativeWindow=$windowName;NativeWindowStart=$windowStart;Variant=$file.BaseName;File=$file.Name;Sha256=(Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash;Rate=$rate;Channels=$channels;SignalChannel=$channel;PlayableFrames=$frames;PeakFrame=$peak;OnsetFrame=$onset}
  } finally {[void][BassReference]::BASS_StreamFree($s)}
 }
} finally {[void][BassReference]::BASS_Free()}
[ordered]@{BassVersion='2.4.15.2';OldGaps=1;Cases=$cases} | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $OutputDirectory 'mp3-bass-timing-reference.json') -Encoding UTF8

& pwsh -NoProfile -NonInteractive -File $PSCommandPath -OutputDirectory $OutputDirectory -RawDirectory $RawDirectory -ConvertOnly
if ($LASTEXITCODE -ne 0) {throw "Reference conversion failed."}
