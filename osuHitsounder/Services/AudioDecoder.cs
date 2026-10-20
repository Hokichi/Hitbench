using System.IO;
using NAudio.Vorbis;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace osuHitsounder.Services;

public static class AudioDecoder
{
    public static WaveStream Open(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".ogg" => new VorbisWaveReader(path),
        ".wav" or ".mp3" => new AudioFileReader(path),
        _ => throw new NotSupportedException($"Unsupported audio format: {Path.GetExtension(path)}")
    };
    public static DecodedAudio Decode(string path, CancellationToken token, Func<string, WaveStream>? openDecoder = null)
    {
        token.ThrowIfCancellationRequested();
        using var stream = (openDecoder ?? Open)(path);
        ISampleProvider provider = stream.ToSampleProvider();
        provider = provider.WaveFormat.Channels switch
        {
            1 => new MonoToStereoSampleProvider(provider),
            2 => provider,
            _ => throw new NotSupportedException($"Unsupported channel count: {provider.WaveFormat.Channels}")
        };
        if (provider.WaveFormat.SampleRate != 44_100)
            provider = new WdlResamplingSampleProvider(provider, 44_100);
        var result = new List<float>();
        var buffer = new float[8192];
        int read;
        while ((read = provider.Read(buffer, 0, buffer.Length)) > 0)
        {
            token.ThrowIfCancellationRequested();
            result.AddRange(buffer.AsSpan(0, read).ToArray());
        }
        if (result.Count == 0) throw new InvalidDataException("Audio file is empty.");
        return DecodedAudio.CopyFrom(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(result));
    }

}
