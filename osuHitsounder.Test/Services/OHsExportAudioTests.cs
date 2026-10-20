using NAudio.Wave;
using osuHitsounder.Resources.DataModels;
using osuHitsounder.Services;
namespace osuHitsounder.Test.Services;

public class OHsExportAudioTests
{
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

