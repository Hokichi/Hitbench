using System.IO;
using NAudio.Vorbis;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using System.Runtime.InteropServices;

namespace Hitbench.Services;

public static class AudioDecoder
{
    public static WaveStream Open(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".ogg" => new VorbisWaveReader(path),
        ".wav" => new AudioFileReader(path),
        ".mp3" => new OHsMp3WaveStream(path),
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
        var estimatedSamples = stream.TotalTime.TotalSeconds * 88_200;
        var capacity = double.IsFinite(estimatedSamples) ? (int)Math.Clamp(estimatedSamples, 0, 1_048_576) : 0;
        var result = new List<float>(capacity);
        var buffer = new float[8192];
        int read;
        while ((read = provider.Read(buffer, 0, buffer.Length)) > 0)
        {
            token.ThrowIfCancellationRequested();
            var offset = result.Count;
            result.EnsureCapacity(checked(offset + read));
            CollectionsMarshal.SetCount(result, offset + read);
            buffer.AsSpan(0, read).CopyTo(CollectionsMarshal.AsSpan(result)[offset..]);
        }
        if (result.Count == 0) throw new InvalidDataException("Audio file is empty.");
        return DecodedAudio.TakeOwnership(result.ToArray());
    }

}
