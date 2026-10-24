using System.Text.Json;
using System.Security.Cryptography;
using NAudio.Wave;
using Hitbench.Services;
using Hitbench.Services.FileSystem;
using Hitbench.Core.Entities;

namespace Hitbench.Test.Services;

public class OHsMp3TimingTests
{
    private static string Fixture(string name) => Path.Combine(TestContext.CurrentContext.TestDirectory, "Fixtures", "Audio", name);
    public static IEnumerable<TestCaseData> Cases()
    {
        using var json = JsonDocument.Parse(File.ReadAllText(Fixture("mp3-bass-timing-reference.json")).TrimStart('\uFEFF'));
        foreach (var item in json.RootElement.GetProperty("Cases").EnumerateArray())
            yield return new TestCaseData(item.Clone()).SetName("BassTiming_" + item.GetProperty("File").GetString());
    }

    [TestCaseSource(nameof(Cases))]
    public void NativeTimingMatchesIndependentBass(JsonElement reference)
    {
        var path = Fixture(reference.GetProperty("File").GetString()!);
        Assert.That(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))), Is.EqualTo(reference.GetProperty("Sha256").GetString()));
        using var stream = AudioDecoder.Open(path);
        var provider = stream.ToSampleProvider();
        var buffer = new float[8192];
        var channels = reference.GetProperty("Channels").GetInt32();
        Assert.That(stream.WaveFormat.SampleRate, Is.EqualTo(reference.GetProperty("Rate").GetInt32()));
        Assert.That(stream.WaveFormat.Channels, Is.EqualTo(channels));
        long frames = 0, peak = -1, onset = -1;
        var maximum = 0f;
        var channel = reference.GetProperty("SignalChannel").GetInt32();
        int read;
        while ((read = provider.Read(buffer, 0, buffer.Length)) > 0)
        {
            for (var i = channel; i < read; i += channels)
            {
                var value = Math.Abs(buffer[i]);
                if (value > maximum) { maximum = value; peak = frames + i / channels; }
                if (onset < 0 && value > .0001f) onset = frames + i / channels;
            }
            frames += read / channels;
        }
        Assert.Multiple(() =>
        {
            Assert.That(frames, Is.EqualTo(reference.GetProperty("PlayableFrames").GetInt64()).Within(1));
            Assert.That(stream.Length / stream.WaveFormat.BlockAlign, Is.EqualTo(frames));
            Assert.That(peak, Is.EqualTo(reference.GetProperty("PeakFrame").GetInt64()).Within(1));
            TestContext.Out.WriteLine($"Threshold onset (diagnostic): actual={onset}, BASS={reference.GetProperty("OnsetFrame").GetInt64()}");
        });
    }

    private static float[] ReadSamples(ISampleProvider provider)
    {
        var result = new List<float>(); var buffer = new float[8192]; int read;
        while ((read = provider.Read(buffer, 0, buffer.Length)) > 0) result.AddRange(buffer.AsSpan(0, read).ToArray());
        return result.ToArray();
    }

    private static void CheckSignal(float[] samples, JsonElement reference, bool converted)
    {
        var channels = converted ? 2 : reference.GetProperty("Channels").GetInt32();
        var channel = reference.GetProperty("SignalChannel").GetInt32();
        var bytes = File.ReadAllBytes(Fixture(reference.GetProperty(converted ? "ConvertedWindow" : "NativeWindow").GetString()!));
        Assert.That(Convert.ToHexString(SHA256.HashData(bytes)), Is.EqualTo(reference.GetProperty(converted ? "ConvertedWindowSha256" : "NativeWindowSha256").GetString()));
        var window = new float[bytes.Length / 4]; Buffer.BlockCopy(bytes, 0, window, 0, bytes.Length);
        var start = reference.GetProperty(converted ? "ConvertedWindowStart" : "NativeWindowStart").GetInt32();
        double best = double.NegativeInfinity; var lag = int.MaxValue;
        for (var shift = -1024; shift <= 1024; shift++)
        {
            if (start + shift < 0 || (start + shift) * channels + window.Length > samples.Length) continue;
            double dot = 0, a = 0, b = 0;
            for (var i = channel; i < window.Length; i += channels)
            {
                var x = samples[(start + shift) * channels + i]; var y = window[i];
                dot += x * (double)y; a += x * (double)x; b += y * (double)y;
            }
            var score = a * b > 0 ? dot / Math.Sqrt(a * b) : -1;
            if (score > best) { best = score; lag = shift; }
        }
        Assert.That(best, Is.GreaterThan(.99));
        Assert.That(lag, Is.InRange(converted ? -2 : -1, converted ? 2 : 1));
    }

    [TestCaseSource(nameof(Cases))]
    public void ReferenceWindowsAndConversionMatch(JsonElement reference)
    {
        using (var stream = AudioDecoder.Open(Fixture(reference.GetProperty("File").GetString()!)))
            CheckSignal(ReadSamples(stream.ToSampleProvider()), reference, false);
        var decoded = AudioDecoder.Decode(Fixture(reference.GetProperty("File").GetString()!), default);
        Assert.That(decoded.FrameCount, Is.EqualTo(reference.GetProperty("ConvertedFrames").GetInt32()).Within(2));
        CheckSignal(decoded.Samples.ToArray(), reference, true);
    }

    [TestCase(8000)] [TestCase(22050)] [TestCase(48000)]
    public void ChunkedReadsAndSeeksPreservePlayableBytes(int rate)
    {
        var path = Fixture($"timing-{rate}-song100.mp3");
        using var source = AudioDecoder.Open(path); using var expectedStream = new MemoryStream(); source.CopyTo(expectedStream);
        var expected = expectedStream.ToArray();
        foreach (var size in new[] { 1, 3, 7, 8192 })
        {
            using var stream = AudioDecoder.Open(path); using var result = new MemoryStream(); var block = new byte[size + 4]; int read;
            Assert.That(stream.Read(block, 2, 0), Is.Zero);
            while ((read = stream.Read(block, 2, size)) > 0) result.Write(block, 2, read);
            Assert.That(result.ToArray(), Is.EqualTo(expected));
            Assert.That(block[0], Is.Zero); Assert.That(block[^1], Is.Zero);
            foreach (var target in new long[] { stream.Length, 0, stream.WaveFormat.BlockAlign, stream.Length / 2, stream.Length - stream.WaveFormat.BlockAlign, -1, long.MaxValue })
            {
                stream.Position = target; var position = stream.Position;
                read = stream.Read(block, 2, size);
                Assert.That(block.AsSpan(2, read).ToArray(), Is.EqualTo(expected.AsSpan((int)position, read).ToArray()));
            }
            Assert.Throws<ArgumentOutOfRangeException>(() => { _ = stream.Read(block, -1, 1); });
            stream.Dispose(); Assert.Throws<ObjectDisposedException>(() => { _ = stream.Read(block, 0, 1); });
        }
    }

    [Test]
    public void InvalidOpenAndCancelledDecodeReleaseFiles()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".mp3");
        try
        {
            File.WriteAllBytes(path, new byte[32]);
            Assert.Catch(() => AudioDecoder.Open(path));
            using (File.Open(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) { }
            Assert.Catch<OperationCanceledException>(() => AudioDecoder.Decode(path, new CancellationToken(true)));
        }
        finally { File.Delete(path); }
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task WaveformMetadataPreviewAndPreparedPlaybackShareOrigin(bool wavSong)
    {
        var path = Fixture("timing-48000-song100.mp3");
        var browser = new SampleBrowserService();
        var metadata = await browser.ReadMetadataAsync(path, default);
        var waveform = await browser.ReadWaveformAsync(path, default);
        Assert.That(metadata.Duration.TotalSeconds, Is.EqualTo(1.4).Within(1d/48000));
        Assert.That(waveform.Duration, Is.EqualTo(metadata.Duration));
        var output = new StubAudioOutput();
        await using var preview = new SamplePreviewService(AudioDecoder.Open, [new("stub", () => output)]);
        await preview.PlayAsync(path, 100, default, TimeSpan.FromSeconds(1));
        var previewSamples = ReadSamples(output.Source!);
        var peak = Enumerable.Range(0, previewSamples.Length / 2).MaxBy(i => Math.Abs(previewSamples[i*2]));
        Assert.That(peak, Is.EqualTo(4800).Within(1));
        await preview.SeekAsync(TimeSpan.Zero, default);
        Assert.That((await preview.GetSnapshotAsync(default)).Position, Is.EqualTo(TimeSpan.Zero));
        var sampleName = wavSong ? "sample.mp3" : "sample.wav";
        using var project = new TestProjectFolder(sampleName);
        File.Copy(Fixture(wavSong ? "timing-48000-sample100.mp3" : "timing-48000-sample100.wav"), Path.Combine(Path.GetDirectoryName(project.ProjectPath)!, "Samples", sampleName), true);
        var player = new StubWavePlayer();
        using var service = new WorkstationAudioService(AudioDecoder.Open, () => player);
        service.Open(wavSong ? Fixture("timing-48000-song100.wav") : path);
        await service.UpdateSamplesAsync(project.ProjectPath, [new SamplePlaybackEvent(1, sampleName, 1000, 100)], default);
        service.Play();
        var mixed = ReadSamples(player.Provider!.ToSampleProvider());
        var left = Enumerable.Range(0, mixed.Length/2).MaxBy(i => Math.Abs(mixed[i*2]));
        var right = Enumerable.Range(0, mixed.Length/2).MaxBy(i => Math.Abs(mixed[i*2+1]));
        Assert.That(left, Is.EqualTo(48510).Within(2));
        Assert.That(right, Is.EqualTo(left).Within(2));
        var seek = TimeSpan.FromMilliseconds(750); service.Seek(seek);
        var after = ReadSamples(player.Provider!.ToSampleProvider());
        Assert.That(after, Is.EqualTo(mixed[66150..]));
    }

    [Test]
    public async Task Mp3ExportUsesIndependentConvertedTimeline()
    {
        using var project = new TestProjectFolder("input.mp3");
        var root = Path.GetDirectoryName(project.ProjectPath)!;
        File.Copy(Fixture("timing-48000-song100.mp3"), Path.Combine(root, "Samples", "input.mp3"), true);
        var output = Directory.CreateDirectory(Path.Combine(root, "export")).FullName;
        var asset = Hitbench.Resources.DataModels.OHsExportAsset.Create([("input.mp3", 100)]);
        var slot = new Hitbench.Resources.DataModels.OHsExportSlot("Normal", Hitbench.Core.Enums.SampleType.Normal);
        var plan = new Hitbench.Resources.DataModels.OHsExportPlan([], [],
            new Dictionary<int, IReadOnlyDictionary<Hitbench.Resources.DataModels.OHsExportSlot, Hitbench.Resources.DataModels.OHsExportAsset>>
            { [1] = new Dictionary<Hitbench.Resources.DataModels.OHsExportSlot, Hitbench.Resources.DataModels.OHsExportAsset> { [slot] = asset } }, 0);
        await OHsExportAudio.WriteAsync(project.ProjectPath, output, plan, new("Hitsounds", 1, ".wav", ".wav", false), default);
        var result = AudioDecoder.Decode(Path.Combine(output, "normal-hitnormal.wav"), default);
        Assert.That(result.FrameCount, Is.EqualTo(61740).Within(2));
        var samples = result.Samples.ToArray();
        var peak = Enumerable.Range(0, result.FrameCount).MaxBy(i => Math.Abs(samples[i*2]));
        Assert.That(peak, Is.EqualTo(48510).Within(2));
    }

    [Test]
    public void OversizedGapMetadataAndTruncatedAudioAreBounded()
    {
        var bytes = File.ReadAllBytes(Fixture("timing-8000-sample0.mp3"));
        var xing = bytes.AsSpan().IndexOf("Xing"u8);
        var flags = System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(xing+4,4));
        var tag = xing+8+((flags&1)!=0?4:0)+((flags&2)!=0?4:0)+((flags&4)!=0?100:0)+((flags&8)!=0?4:0);
        bytes.AsSpan(tag+21,3).Fill(255);
        var path=Path.Combine(Path.GetTempPath(),Guid.NewGuid()+".mp3");
        try
        {
            File.WriteAllBytes(path, bytes);
            using (var stream=AudioDecoder.Open(path))
            {
                Assert.That(stream.Length, Is.GreaterThan(0));
                using var decoded=new MemoryStream();stream.CopyTo(decoded);
                Assert.That(decoded.Length, Is.EqualTo(stream.Length));
            }
            File.WriteAllBytes(path, bytes[..(xing+6)]);
            Assert.Catch(() => AudioDecoder.Open(path));
            using var exclusive=File.Open(path,FileMode.Open,FileAccess.ReadWrite,FileShare.None);
        }
        finally {File.Delete(path);}
    }
}
