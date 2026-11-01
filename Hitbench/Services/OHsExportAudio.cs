using System.IO;
using NAudio.Wave;
using OggVorbisEncoder;
using Hitbench.Resources.DataModels;
using Hitbench.Services.FileSystem;
namespace Hitbench.Services;

public static class OHsExportAudio
{
    public static DecodedAudio Render(OHsExportAsset asset, Func<string, DecodedAudio> resolve, CancellationToken token, AudioMemoryBudget? budget = null)
    {
        token.ThrowIfCancellationRequested();
        var inputs = asset.Components.Select(s => (Audio: resolve(s.FileName), Gain: s.GainPercent / 100f, s.Envelope)).ToArray();
        budget ??= inputs.Select(input => input.Audio.Budget).FirstOrDefault(value => value is not null) ?? new();
        foreach (var input in inputs) input.Audio.AttachBudget(budget);
        var sampleCount = inputs.Length == 0 ? 882 : checked(inputs.Max(s => s.Audio.FrameCount) * 2);
        AudioMemoryReservation? charge = budget.Reserve(checked((long)sampleCount * sizeof(float)));
        try
        {
            var sum = new float[sampleCount];
            foreach (var input in inputs)
                for (var i = 0; i < input.Audio.Samples.Length; i++)
                {
                    if ((i & 8191) == 0) token.ThrowIfCancellationRequested();
                    var value = input.Audio.Samples[i];
                    if (!float.IsFinite(value) || input.Gain < 0) throw new InvalidDataException("Sample PCM/gain is invalid.");
                    sum[i] += value * input.Gain * (float)(input.Envelope?.GetGain(i / 2) ?? 1);
                }
            if (sum.Length == 0 || sum.Any(v => !float.IsFinite(v)))
                throw new InvalidDataException($"Exported sample contains invalid PCM: {string.Join(", ", asset.Sources.Select(s => $"{s.FileName} ({s.GainPercent}%)"))}.");
            for (var i = 0; i < sum.Length; i++)
            {
                if ((i & 8191) == 0) token.ThrowIfCancellationRequested();
                sum[i] = Math.Clamp(sum[i], -1f, 1f);
            }
            var result = DecodedAudio.TakeOwnership(sum, charge);
            charge = null;
            return result;
        }
        finally { charge?.Dispose(); }
    }
    public static async Task WriteAsync(string projectPath, string stagingDirectory, OHsExportPlan plan, OHsExportOptions options, CancellationToken token,
        Func<string, CancellationToken, DecodedAudio>? decode = null, IProgress<OHsExportProgress>? progress = null, AudioMemoryBudget? budget = null)
    {
        budget ??= new();
        var decoded = new Dictionary<string, DecodedAudio>(StringComparer.OrdinalIgnoreCase);
        var encoded = new Dictionary<(string Id, string Format), string>();
        var total = plan.Banks.Values.Sum(b => b.Count);
        var finished = 0;
        void ReportOutput() => progress?.Report(new(40 + 45d * ++finished / total, "Generating samples"));
        DecodedAudio Resolve(string file)
        {
            if (!decoded.TryGetValue(file, out var audio))
            {
                audio = decode is null ? AudioDecoder.Decode(SamplePaths.Resolve(projectPath, file), token, budget: budget)
                    : decode(SamplePaths.Resolve(projectPath, file), token);
                try { audio.AttachBudget(budget); decoded.Add(file, audio); }
                catch { audio.Dispose(); throw; }
            }
            return audio;
        }
        try
        {
            foreach (var bank in plan.Banks.OrderBy(p => p.Key))
                foreach (var slot in bank.Value)
                {
                    token.ThrowIfCancellationRequested();
                    var asset = slot.Value;
                    var extension = asset.Sources.Count > 1 ? options.MixedFormat : options.SingleFormat;
                    var output = Path.Combine(stagingDirectory, OHsExportRepresentations.FileName(slot.Key, bank.Key, extension));
                    if (encoded.TryGetValue((asset.Id, extension), out var existing)) { File.Copy(existing, output); ReportOutput(); continue; }
                    var single = asset.Sources.Count == 1 ? asset.Sources[0] : default;
                    using var rendered = asset.Sources.Count == 1 && single.GainPercent == 100 && asset.Components[0].Envelope is null ? null : Render(asset, Resolve, token, budget);
                    var pcm = rendered ?? Resolve(single.FileName);
                    ValidatePcm(pcm);
                    var source = asset.Sources.Count == 1 ? SamplePaths.Resolve(projectPath, single.FileName) : null;
                    if (single.GainPercent == 100 && asset.Components[0].Envelope is null && source is not null && MatchesEncoding(source, extension))
                    {
                        await using var input = File.OpenRead(source); await using var destination = File.Create(output);
                        await input.CopyToAsync(destination, token);
                    }
                    else await WriteFileAsync(output, pcm, token, budget: budget);
                    encoded[(asset.Id, extension)] = output;
                    ReportOutput();
                }
            progress?.Report(new(85, "Writing difficulty"));
        }
        finally { foreach (var audio in decoded.Values) audio.Dispose(); }
    }
    public static Task WriteFileAsync(string path, DecodedAudio audio, CancellationToken token, Action<IWaveProvider, string>? mp3Encoder = null, AudioMemoryBudget? budget = null) => Task.Run(() =>
    {
        token.ThrowIfCancellationRequested();
        ValidatePcm(audio);
        budget ??= audio.Budget ?? new();
        using var encodingScratch = budget.Reserve(32768);
        var pcm = new CancellationWaveProvider(new DecodedAudioSampleProvider(audio).ToWaveProvider16(), token);
        switch (Path.GetExtension(path).ToLowerInvariant())
        {
            case ".wav":
                using (var writer = new WaveFileWriter(path, pcm.WaveFormat))
                {
                    var block = new byte[8192];
                    int read;
                    while ((read = pcm.Read(block, 0, block.Length)) > 0) writer.Write(block, 0, read);
                }
                break;
            case ".mp3": (mp3Encoder ?? ((provider, output) => MediaFoundationEncoder.EncodeToMp3(provider, output, 192000)))(pcm, path); break;
            case ".ogg": WriteVorbis(path, audio.Samples, token); break;
            default: throw new ArgumentException("Unsupported export format.");
        }
        token.ThrowIfCancellationRequested();
    }, token);
    private static void ValidatePcm(DecodedAudio audio)
    {
        if (audio.Samples.Length == 0) throw new InvalidDataException("Invalid export PCM.");
        foreach (var value in audio.Samples)
            if (!float.IsFinite(value) || value is < -1 or > 1) throw new InvalidDataException("Invalid export PCM.");
    }
    private static bool MatchesEncoding(string path, string format)
    {
        using var source = File.OpenRead(path); Span<byte> header = stackalloc byte[12];
        var count = source.Read(header);
        return format switch
        {
            ".wav" => count == 12 && header[..4].SequenceEqual("RIFF"u8) && header[8..12].SequenceEqual("WAVE"u8),
            ".ogg" => count >= 4 && header[..4].SequenceEqual("OggS"u8),
            ".mp3" => count >= 3 && (header[..3].SequenceEqual("ID3"u8) || header[0] == 255 && (header[1] & 224) == 224),
            _ => false
        };
    }
    private static void WriteVorbis(string path, ReadOnlySpan<float> samples, CancellationToken token)
    {
        var info = VorbisInfo.InitVariableBitRate(2, 44100, .5f);
        var stream = new OggStream(Random.Shared.Next());
        stream.PacketIn(HeaderPacketBuilder.BuildInfoPacket(info));
        stream.PacketIn(HeaderPacketBuilder.BuildCommentsPacket(new Comments()));
        stream.PacketIn(HeaderPacketBuilder.BuildBooksPacket(info));
        using var output = File.Create(path);
        void Flush(bool force)
        {
            while (stream.PageOut(out var page, force))
            { token.ThrowIfCancellationRequested(); output.Write(page.Header); output.Write(page.Body); }
        }
        Flush(true);
        var channels = new[] { new float[512], new float[512] };
        var totalFrames = samples.Length / 2;
        var state = ProcessingState.Create(info);
        // The installed encoder starts its input at zero rather than the initial
        // window center. Prime that codec overlap; it is not part of the sample.
        var overlap = info.CodecSetup.BlockSizes[1] / 2;
        for (var offset = 0; offset < overlap; offset += 512)
            state.WriteData(channels, Math.Min(512, overlap - offset), 0);
        void Packets()
        {
            while (!stream.Finished && state.PacketOut(out var packet))
            {
                token.ThrowIfCancellationRequested();
                // OggVorbisEncoder 1.2.2 reports the previous block's position at EOF.
                // Supply the actual PCM length so decoders retain short samples and tails.
                if (packet.EndOfStream) packet = new OggPacket(packet.PacketData, true, totalFrames, packet.PacketNumber);
                stream.PacketIn(packet); Flush(false);
            }
        }
        for (var offset = 0; offset < samples.Length / 2; offset += 512)
        {
            token.ThrowIfCancellationRequested();
            var count = Math.Min(512, samples.Length / 2 - offset);
            for (var i = 0; i < count; i++) { channels[0][i] = samples[(offset + i) * 2]; channels[1][i] = samples[(offset + i) * 2 + 1]; }
            state.WriteData(channels, count, 0); Packets();
        }
        state.WriteEndOfStream(); Packets(); Flush(true);
    }
}
