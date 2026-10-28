using System.IO;
using NAudio.Vorbis;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using System.Runtime.InteropServices;

namespace Hitbench.Services;

public static class AudioDecoder
{
    public static WaveStream Open(string path) => Open(path, new AudioMemoryBudget());
    public static WaveStream Open(string path, AudioMemoryBudget budget) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".ogg" => new VorbisWaveReader(path),
        ".wav" => new AudioFileReader(path),
        ".mp3" => new OHsMp3WaveStream(path, budget),
        _ => throw new NotSupportedException($"Unsupported audio format: {Path.GetExtension(path)}")
    };
    public static DecodedAudio Decode(string path, CancellationToken token, Func<string, WaveStream>? openDecoder = null, AudioMemoryBudget? budget = null)
    {
        budget ??= new();
        token.ThrowIfCancellationRequested();
        using var stream = openDecoder is null ? Open(path, budget) : openDecoder(path);
        ISampleProvider provider = stream.ToSampleProvider();
        provider = provider.WaveFormat.Channels switch
        {
            1 => new MonoToStereoSampleProvider(provider),
            2 => provider,
            _ => throw new NotSupportedException($"Unsupported channel count: {provider.WaveFormat.Channels}")
        };
        if (provider.WaveFormat.SampleRate != 44_100)
            provider = new WdlResamplingSampleProvider(provider, 44_100);
        using var scratch = budget.Reserve(8192 * sizeof(float));
        var buffer = new float[8192];
        var chunks = new List<(float[] Values, AudioMemoryReservation Charge)>();
        AudioMemoryReservation? final = null;
        var count = 0;
        try
        {
            int read;
            while ((read = provider.Read(buffer, 0, buffer.Length)) > 0)
            {
                token.ThrowIfCancellationRequested();
                count = checked(count + read);
                budget.CheckAsset(checked((long)count * sizeof(float)));
                var charge = budget.Reserve(checked((long)read * sizeof(float)));
                try { chunks.Add((buffer.AsSpan(0, read).ToArray(), charge)); }
                catch { charge.Dispose(); throw; }
            }
            token.ThrowIfCancellationRequested();
            if (count == 0) throw new InvalidDataException("Audio file is empty.");
            if (count % 2 != 0) throw new InvalidDataException("Audio contains an incomplete stereo frame.");
            final = budget.Reserve(checked((long)count * sizeof(float)));
            var result = new float[count];
            var offset = 0;
            foreach (var chunk in chunks)
            {
                token.ThrowIfCancellationRequested();
                chunk.Values.CopyTo(result, offset); offset += chunk.Values.Length;
            }
            var audio = DecodedAudio.TakeOwnership(result, final);
            final = null;
            return audio;
        }
        finally
        {
            final?.Dispose();
            foreach (var chunk in chunks) chunk.Charge.Dispose();
        }
    }

}
