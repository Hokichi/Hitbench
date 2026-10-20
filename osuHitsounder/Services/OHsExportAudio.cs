using System.IO;
using NAudio.Wave;
using OggVorbisEncoder;
using osuHitsounder.Resources.DataModels;
using osuHitsounder.Services.FileSystem;
namespace osuHitsounder.Services;

public static class OHsExportAudio
{
    public static DecodedAudio Render(OHsExportAsset asset, Func<string, DecodedAudio> resolve, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (asset.Sources.Count == 0) return DecodedAudio.CopyFrom(new float[882]);
        var inputs = asset.Sources.Select(s => (Audio: resolve(s.FileName), Gain: s.GainPercent / 100f)).ToArray();
        var sum = new float[inputs.Max(s => s.Audio.FrameCount) * 2];
        foreach (var input in inputs)
            for (var i = 0; i < input.Audio.Samples.Length; i++)
            {
                if ((i & 8191) == 0) token.ThrowIfCancellationRequested();
                var value = input.Audio.Samples[i];
                if (!float.IsFinite(value) || input.Gain < 0) throw new InvalidDataException("Sample PCM/gain is invalid.");
                sum[i] += value * input.Gain;
            }
        if (sum.Length == 0 || sum.Any(v => !float.IsFinite(v) || v is < -1 or > 1)) throw new InvalidDataException("Exported sample would clip or contains invalid PCM.");
        return DecodedAudio.CopyFrom(sum);
    }
    public static async Task WriteAsync(string projectPath, string stagingDirectory, OHsExportPlan plan, OHsExportOptions options, CancellationToken token,
        Func<string, CancellationToken, DecodedAudio>? decode = null)
    {
        var decoded = new Dictionary<string, DecodedAudio>(StringComparer.OrdinalIgnoreCase);
        var encoded = new Dictionary<(string Id, string Format), string>();
        foreach (var bank in plan.Banks.OrderBy(p => p.Key))
            foreach (var slot in bank.Value)
            {
                token.ThrowIfCancellationRequested();
                var asset = slot.Value;
                var extension = asset.Sources.Count > 1 ? options.MixedFormat : options.SingleFormat;
                var output = Path.Combine(stagingDirectory, OHsExportRepresentations.FileName(slot.Key, bank.Key, extension));
                if (encoded.TryGetValue((asset.Id, extension), out var existing)) { File.Copy(existing, output); continue; }
                var pcm = Render(asset, file =>
                {
                    if (!decoded.TryGetValue(file, out var audio)) decoded[file] = audio = (decode ?? ((path, cancellation) => AudioDecoder.Decode(path, cancellation)))(SamplePaths.Resolve(projectPath, file), token);
                    return audio;
                }, token);
                var single = asset.Sources.Count == 1 ? asset.Sources[0] : default;
                var source = asset.Sources.Count == 1 ? SamplePaths.Resolve(projectPath, single.FileName) : null;
                if (single.GainPercent == 100 && source is not null && MatchesEncoding(source, extension))
                {
                    await using var input = File.OpenRead(source); await using var destination = File.Create(output);
                    await input.CopyToAsync(destination, token);
                }
                else await WriteFileAsync(output, pcm, token);
                encoded[(asset.Id, extension)] = output;
            }
    }
    public static Task WriteFileAsync(string path, DecodedAudio audio, CancellationToken token, Action<IWaveProvider, string>? mp3Encoder = null) => Task.Run(() =>
    {
        token.ThrowIfCancellationRequested();
        var samples = audio.Samples.ToArray();
        if (samples.Length == 0 || samples.Any(v => !float.IsFinite(v) || v is < -1 or > 1)) throw new InvalidDataException("Invalid export PCM.");
        var bytes = new byte[samples.Length * sizeof(float)]; Buffer.BlockCopy(samples, 0, bytes, 0, bytes.Length);
        using var raw = new RawSourceWaveStream(new MemoryStream(bytes, false), WaveFormat.CreateIeeeFloatWaveFormat(44100, 2));
        var pcm = new CancellationWaveProvider(raw.ToSampleProvider().ToWaveProvider16(), token);
        switch (Path.GetExtension(path).ToLowerInvariant())
        {
            case ".wav": WaveFileWriter.CreateWaveFile(path, pcm); break;
            case ".mp3": (mp3Encoder ?? ((provider, output) => MediaFoundationEncoder.EncodeToMp3(provider, output, 192000)))(pcm, path); break;
            case ".ogg": WriteVorbis(path, samples, token); break;
            default: throw new ArgumentException("Unsupported export format.");
        }
        token.ThrowIfCancellationRequested();
    }, token);
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
    private static void WriteVorbis(string path, float[] samples, CancellationToken token)
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
        var channels = new[] { new float[samples.Length / 2], new float[samples.Length / 2] };
        for (var i = 0; i < samples.Length / 2; i++) { channels[0][i] = samples[i * 2]; channels[1][i] = samples[i * 2 + 1]; }
        var state = ProcessingState.Create(info);
        void Packets()
        {
            while (!stream.Finished && state.PacketOut(out var packet))
            { token.ThrowIfCancellationRequested(); stream.PacketIn(packet); Flush(false); }
        }
        for (var offset = 0; offset < channels[0].Length; offset += 512)
        { token.ThrowIfCancellationRequested(); state.WriteData(channels, Math.Min(512, channels[0].Length - offset), offset); Packets(); }
        state.WriteEndOfStream(); Packets(); Flush(true);
    }
}
