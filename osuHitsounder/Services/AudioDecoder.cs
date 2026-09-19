using System.IO;
using NAudio.Vorbis;
using NAudio.Wave;

namespace osuHitsounder.Services;

public static class AudioDecoder
{
    public static WaveStream Open(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".ogg" => new VorbisWaveReader(path),
        ".wav" or ".mp3" => new AudioFileReader(path),
        _ => throw new NotSupportedException($"Unsupported audio format: {Path.GetExtension(path)}")
    };
}
