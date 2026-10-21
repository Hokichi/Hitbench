tone.mp3 and tone.ogg are original generated test fixtures.

Generated on 2026-09-19 with FFmpeg's built-in sine source:
  sine=frequency=440:duration=0.15, mono, 8000 Hz

They contain no third-party recording or copyrighted musical work. Tests decode
them to validate MP3 and Ogg waveform extraction without opening an audio device.

timing-* fixtures are original synthetic stereo impulses (amplitude 0.5).
Regenerate using: powershell -File generate-timing-fixtures.ps1
FFmpeg/ffprobe are used only when generating, never by tests.
timing-encoder.txt records the encoder version; timing-manifest.json records
the verified native sample rate, original impulse frame, channel and role.
song0/song100 place a left-channel impulse at 1/1.1 seconds in 1.4 seconds of PCM.
sample0/sample100 place a right-channel impulse at 0/0.1 seconds in 0.3 seconds.
Positions are rounded to the nearest native frame, halfway away from zero.
WAV is float32; OGG uses libvorbis quality 4; MP3 uses libmp3lame quality 2.
MP3 variants include default Xing metadata and -write_xing 0. MP3 rates stop
at 48 kHz; WAV and OGG also cover 88.2 and 96 kHz. No encoder rate substitution
is allowed. These are timing probes, not assumptions of lossless codec output.

MP3 compatibility references (2026-10-21)
---------------------------------------
generate-mp3-timing-references.ps1 generates only the missing compatibility
variants and records independent BASS 2.4.15.2 output with OLDGAPS=1. Run it
using 32-bit Windows PowerShell with -BassDllPath and -OutputDirectory; its
conversion stage calls 64-bit pwsh and the already-built NAudio.Core.dll.
FFmpeg is used offline for mono CBR/Info and VBR/Xing variants. A delay288
variant changes the synthetic encoder delay and compensates padding; the
BASS reference confirms its interpretation. An unknown-identifier extension
verifies that a valid gapless field layout is not limited to LAME/Lavc names.

mp3-bass-timing-reference.json records source hashes, native format, lengths,
peaks and onset diagnostics, plus hashed native and converted signal windows.
*.bass-window.f32 stores interleaved native floating PCM; *.converted-window.f32
stores BASS-origin PCM resampled by WDL to 44,100 Hz stereo. Only original
synthetic signals are included. Threshold-onset differences are diagnostic:
16-bit ACM quantization must not be confused with a change in time origin.
Routine tests require neither BASS, FFmpeg, an osu! installation nor hardware.
Raw full reference PCM remains in the system temporary directory, outside Git.
