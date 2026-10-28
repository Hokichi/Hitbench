using System.IO;
using NAudio.Vorbis;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

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
        var stream = openDecoder is null ? Open(path, budget) : openDecoder(path);
        DecodedAudio? decoded = null;
        var disposalAttempted = false;
        try
        {
            decoded = DecodePcm(stream, token, budget);
            disposalAttempted = true;
            stream.Dispose();
            return decoded;
        }
        catch
        {
            decoded?.Dispose();
            if (!disposalAttempted)
            {
                try { stream.Dispose(); }
                catch (Exception cleanup) { System.Diagnostics.Trace.TraceWarning("Audio decoder cleanup: {0}", cleanup); }
            }
            throw;
        }
    }

    private static DecodedAudio DecodePcm(WaveStream stream, CancellationToken token, AudioMemoryBudget budget)
    {
        // Raw seekable PCM has a physical length; other codecs/hints remain incremental.
        if (stream is RawSourceWaveStream && stream.CanSeek && stream.WaveFormat.SampleRate == 44_100 &&
            stream.Length / stream.WaveFormat.BlockAlign > budget.MaximumAssetBytes / 8)
            throw new AudioMemoryLimitException("Decoded audio exceeds the asset memory limit.");
        ISampleProvider provider = stream as ISampleProvider ?? stream.ToSampleProvider();
        provider = provider.WaveFormat.Channels switch
        {
            1 => new MonoToStereoSampleProvider(provider),
            2 => provider,
            _ => throw new NotSupportedException($"Unsupported channel count: {provider.WaveFormat.Channels}")
        };
        if (provider.WaveFormat.SampleRate != 44_100)
            provider = new WdlResamplingSampleProvider(provider, 44_100);
        // Include bounded conversion/resampling workspaces before the first read.
        var resampling = stream.WaveFormat.SampleRate != 44_100;
        var inputSamples = resampling ? Math.Max(8192L, checked((8192L * stream.WaveFormat.SampleRate + 44_099) / 44_100 + 64)) : 8192L;
        using var scratch = budget.Reserve(checked((8192L + (resampling ? 4 : 3) * inputSamples) * sizeof(float)));
        var buffer = new float[8192];
        var chunks = new List<(float[] Values, AudioMemoryReservation Charge)>();
        AudioMemoryReservation? final = null;
        var count = 0;
        var filled = buffer.Length;
        try
        {
            int read;
            while ((read = provider.Read(buffer, 0, buffer.Length)) > 0)
            {
                token.ThrowIfCancellationRequested();
                count = checked(count + read);
                budget.CheckAsset(checked((long)count * sizeof(float)));
                var sourceOffset = 0;
                while (sourceOffset < read)
                {
                    if (filled == buffer.Length)
                    {
                        var charge = budget.Reserve(checked((long)buffer.Length * sizeof(float)));
                        try { chunks.Add((new float[buffer.Length], charge)); }
                        catch { charge.Dispose(); throw; }
                        filled = 0;
                    }
                    var length = Math.Min(read - sourceOffset, buffer.Length - filled);
                    buffer.AsSpan(sourceOffset, length).CopyTo(chunks[^1].Values.AsSpan(filled));
                    filled += length; sourceOffset += length;
                }
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
                var length = Math.Min(chunk.Values.Length, count - offset);
                chunk.Values.AsSpan(0, length).CopyTo(result.AsSpan(offset)); offset += length;
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
