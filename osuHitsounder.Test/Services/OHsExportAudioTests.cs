using NAudio.Wave;
using osuHitsounder.Resources.DataModels;
using osuHitsounder.Services;
namespace osuHitsounder.Test.Services;

public class OHsExportAudioTests
{
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
    public void SilenceAndDuplicateGainAndClippingAreExplicit()
    {
        Assert.That(OHsExportAudio.Render(OHsExportAsset.Create([]), _ => throw new Exception(), default).Samples.ToArray(), Is.All.Zero);
        var repeated = OHsExportAsset.Create([("a.wav", 70), ("A.wav", 40)]);
        Assert.That(OHsExportAudio.Render(repeated, _ => DecodedAudio.CopyFrom(new float[] { .2f, -.2f }), default).Samples.ToArray(), Is.EqualTo(new[] { .22f, -.22f }).Within(.00001));
        Assert.Throws<InvalidDataException>(() => OHsExportAudio.Render(repeated, _ => DecodedAudio.CopyFrom(new float[] { 1, -1 }), default));
        Assert.Throws<InvalidDataException>(() => OHsExportAudio.Render(repeated, _ => DecodedAudio.CopyFrom(new float[] { float.NaN, 0 }), default));
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

