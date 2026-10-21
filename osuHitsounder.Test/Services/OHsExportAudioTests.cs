using NAudio.Wave;
using osuHitsounder.Resources.DataModels;
using osuHitsounder.Services;
namespace osuHitsounder.Test.Services;

public class OHsExportAudioTests
{
    [TestCase(.2f, .4f)]
    [TestCase(.75f, 1f)]
    public void TwoHundredPercentGainRetainsExistingPeakClamp(float input, float expected)
    {
        var asset = OHsExportAsset.Create([("hit.wav", 200)]);
        var result = OHsExportAudio.Render(asset, _ => DecodedAudio.CopyFrom(new[] { input, input }), default);
        Assert.That(result.Samples.ToArray(), Is.EqualTo(new[] { expected, expected }).Within(.0001));
    }

    [Test]
    public async Task VorbisRetainsAttackAndTailPositions()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".ogg");
        try
        {
            var pcm = new float[88200];
            foreach (var frame in new[] { 0, 22050, 44090 }) pcm[frame * 2] = pcm[frame * 2 + 1] = .5f;
            await OHsExportAudio.WriteFileAsync(path, DecodedAudio.CopyFrom(pcm), default);
            var decoded = AudioDecoder.Decode(path, default);
            Assert.That(decoded.FrameCount, Is.EqualTo(44100));
            var samples = decoded.Samples.ToArray();
            foreach (var frame in new[] { 0, 22050, 44090 })
            {
                var start = Math.Max(0, frame - 20); var end = Math.Min(44100, frame + 21);
                var peak = Enumerable.Range(start, end - start).MaxBy(i => Math.Abs(samples[i * 2]));
                Assert.That(peak, Is.InRange(Math.Max(0, frame - 2), frame + 2));
                Assert.That(Math.Abs(samples[peak * 2]), Is.GreaterThan(.25f));
            }
        }
        finally { File.Delete(path); }
    }
    [TestCase(1), TestCase(441), TestCase(1024), TestCase(44100)]
    public async Task ShortVorbisSilenceDecodesToItsOriginalFrames(int frames)
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".ogg");
        try
        {
            await OHsExportAudio.WriteFileAsync(path, DecodedAudio.CopyFrom(new float[frames * 2]), default);
            Assert.That(AudioDecoder.Decode(path, default).FrameCount, Is.EqualTo(frames));
        }
        finally { File.Delete(path); }
    }
    [Test]
    public async Task DirectSampleCopyUsesValidatedPcmWithoutRenderingDuplicate()
    {
        using var project = new TestProjectFolder();
        var root = Path.GetDirectoryName(project.ProjectPath)!;
        var stage = Directory.CreateDirectory(Path.Combine(root, "stage")).FullName;
        var audio = DecodedAudio.CopyFrom(new float[44100 * 2 * 10]);
        var source = Path.Combine(root, "Samples", "direct.wav");
        await OHsExportAudio.WriteFileAsync(source, audio, default);
        var asset = OHsExportAsset.Create([("direct.wav", 100)]);
        var slots = new Dictionary<OHsExportSlot, OHsExportAsset> { [new("Normal", osuHitsounder.Core.Enums.SampleType.Normal)] = asset };
        var plan = new OHsExportPlan([], [], new Dictionary<int, IReadOnlyDictionary<OHsExportSlot, OHsExportAsset>> { [1] = slots }, 0);
        var before = GC.GetTotalAllocatedBytes(true);
        await OHsExportAudio.WriteAsync(project.ProjectPath, stage, plan, new("Hitsounds", 1, ".wav", ".wav", false), default, (_, _) => audio);
        var allocated = GC.GetTotalAllocatedBytes(true) - before;
        Assert.That(allocated, Is.LessThan(audio.Samples.Length * sizeof(float)));
        Assert.That(await File.ReadAllBytesAsync(Path.Combine(stage, "normal-hitnormal.wav")), Is.EqualTo(await File.ReadAllBytesAsync(source)));
    }

    [Test]
    public async Task WavEncodingDoesNotDuplicateTheWholePcmBuffer()
    {
        var audio = DecodedAudio.CopyFrom(new float[44100 * 2 * 10]);
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".wav");
        try
        {
            await OHsExportAudio.WriteFileAsync(path, DecodedAudio.CopyFrom(new float[882]), default);
            var before = GC.GetTotalAllocatedBytes(true);
            await OHsExportAudio.WriteFileAsync(path, audio, default);
            var allocated = GC.GetTotalAllocatedBytes(true) - before;
            Assert.That(AudioDecoder.Decode(path, default).FrameCount, Is.EqualTo(audio.FrameCount));
            Assert.That(allocated, Is.LessThan(44100 * 2 * 10 * sizeof(float)), $"Encoding allocated {allocated} bytes");
        }
        finally { File.Delete(path); }
    }

    [Test]
    public void TrackGainsApplyBeforeSummingWithoutAveraging()
    {
        var recipe = OHsExportAsset.Create([("a.wav", 70), ("b.wav", 40)]);
        var audio = OHsExportAudio.Render(recipe, path => DecodedAudio.CopyFrom(path == "a.wav" ? new float[] { .2f, .2f } : new float[] { .1f, .1f, .1f, .1f }), default);
        Assert.That(audio.Samples.ToArray(), Is.EqualTo(new[] { .18f, .18f, .04f, .04f }).Within(.00001));
        Assert.That(OHsExportAudio.Render(OHsExportAsset.Create([("a.wav", 70)]), _ => DecodedAudio.CopyFrom(new float[] { .2f, .2f }), default).Samples.ToArray(), Is.EqualTo(new[] { .14f, .14f }).Within(.00001));
    }
    [Test]
    public void SilenceAndDuplicateGainRemainExplicitWhenPeaksAreCapped()
    {
        Assert.That(OHsExportAudio.Render(OHsExportAsset.Create([]), _ => throw new Exception(), default).Samples.ToArray(), Is.All.Zero);
        var repeated = OHsExportAsset.Create([("a.wav", 70), ("A.wav", 40)]);
        Assert.That(OHsExportAudio.Render(repeated, _ => DecodedAudio.CopyFrom(new float[] { .2f, -.2f }), default).Samples.ToArray(), Is.EqualTo(new[] { .22f, -.22f }).Within(.00001));
        Assert.That(OHsExportAudio.Render(repeated, _ => DecodedAudio.CopyFrom(new float[] { 1, -1 }), default).Samples.ToArray(), Is.EqualTo(new[] { 1f, -1f }));
        Assert.Throws<InvalidDataException>(() => OHsExportAudio.Render(repeated, _ => DecodedAudio.CopyFrom(new float[] { float.NaN, 0 }), default));
    }

    [Test]
    public void MixedSamplesCapOnlyOutOfRangeValues()
    {
        var asset = OHsExportAsset.Create([("a.wav", 100), ("b.wav", 100)]);
        var audio = OHsExportAudio.Render(asset, file => DecodedAudio.CopyFrom(file == "a.wav"
            ? new float[] { .8f, -.8f, .1f, -.1f }
            : new float[] { .7f, -.7f, .2f, -.2f, .4f, -.4f }), default);

        Assert.That(audio.FrameCount, Is.EqualTo(3));
        Assert.That(audio.Samples.ToArray(), Is.EqualTo(new[] { 1f, -1f, .3f, -.3f, .4f, -.4f }).Within(.00001));
    }

    [Test]
    public void MixedSamplesCapAfterAllContributions()
    {
        var asset = OHsExportAsset.Create([("a.wav", 100), ("b.wav", 100), ("c.wav", 100)]);
        var audio = OHsExportAudio.Render(asset, file => DecodedAudio.CopyFrom(file == "c.wav"
            ? new float[] { -.8f, .8f }
            : new float[] { .8f, -.8f }), default);

        Assert.That(audio.Samples.ToArray(), Is.EqualTo(new[] { .8f, -.8f }).Within(.00001));
    }

    [TestCase(float.NaN, 100)]
    [TestCase(float.PositiveInfinity, 100)]
    [TestCase(float.NegativeInfinity, 100)]
    [TestCase(float.MaxValue, 200)]
    [TestCase(.5f, -1)]
    public void InvalidPcmAndGainStillFail(float sample, int gain)
    {
        var asset = OHsExportAsset.Create([("a.wav", gain)]);

        Assert.Throws<InvalidDataException>(() => OHsExportAudio.Render(asset,
            _ => DecodedAudio.CopyFrom(new[] { sample, sample }), default));
    }

    [Test]
    public void EmptyRenderedAudioStillFails()
    {
        var asset = OHsExportAsset.Create([("a.wav", 100)]);

        Assert.Throws<InvalidDataException>(() => OHsExportAudio.Render(asset,
            _ => DecodedAudio.CopyFrom([]), default));
    }

    [Test]
    public void CancelledRenderingDoesNotResolveSources()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var calls = 0;
        var asset = OHsExportAsset.Create([("a.wav", 100)]);

        Assert.Throws<OperationCanceledException>(() => OHsExportAudio.Render(asset, _ =>
        {
            calls++;
            return DecodedAudio.CopyFrom(new float[] { .5f, .5f });
        }, cancellation.Token));
        Assert.That(calls, Is.Zero);
    }

    [TestCase(".wav")]
    [TestCase(".ogg")]
    [TestCase(".mp3")]
    public async Task ActualCodecsDecodeToAudibleStereo(string extension)
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + extension);
        try
        {
            var pcm = Enumerable.Range(0, 44100).SelectMany(i => new[] { (float)(.1 * Math.Sin(2 * Math.PI * 440 * i / 44100)), (float)(.1 * Math.Sin(2 * Math.PI * 440 * i / 44100)) }).ToArray();
            await OHsExportAudio.WriteFileAsync(path, DecodedAudio.CopyFrom(pcm), default);
            var decoded = AudioDecoder.Decode(path, default);
            Assert.That(decoded.FrameCount, Is.InRange(43000, 47000));
            var rms = Math.Sqrt(decoded.Samples.ToArray().Average(v => (double)v * v));
            Assert.That(rms, Is.EqualTo(Math.Sqrt(.005)).Within(.015));
            if (extension == ".wav") Assert.That(decoded.Samples.ToArray(), Is.EqualTo(pcm).Within(2d / 32768));
        }
        finally { File.Delete(path); }
    }
    [Test]
    public void CancelledProviderNeverReadsInput()
    {
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        var provider = new CancellationWaveProvider(new SilenceProvider(WaveFormat.CreateIeeeFloatWaveFormat(44100, 2)), cancellation.Token);
        Assert.Throws<OperationCanceledException>(() => provider.Read(new byte[8], 0, 8));
    }
}
