using osuHitsounder.Services;

namespace osuHitsounder.Test.Services;

public class WaveformPeakReaderTests
{
    [Test]
    public void PreservesStereoPeakInsteadOfCancellingChannels()
    {
        var source = new TestSampleProvider([0.75f, -0.75f, 0.25f, -0.25f], 2, 2);
        var data = new WaveformPeakReader(1, 10).Read(source, CancellationToken.None);
        Assert.That(data.Peaks, Is.EqualTo(new[] { 0.75f, 0.25f }));
        Assert.That(data.Duration, Is.EqualTo(TimeSpan.FromSeconds(1)));
    }

    [Test]
    public void KeepsSilencePartialFinalBinAndIgnoresNonfiniteSamples()
    {
        var source = new TestSampleProvider([0f, 0f, float.NaN, float.PositiveInfinity, -0.5f], 5, 1);
        var data = new WaveformPeakReader(2, 10).Read(source, CancellationToken.None);
        Assert.That(data.Peaks, Is.EqualTo(new[] { 0f, 0f, 0.5f }));
        Assert.That(data.Duration, Is.EqualTo(TimeSpan.FromSeconds(1)));
    }

    [Test]
    public void CompactsLongStreamsWithoutDroppingTransient()
    {
        var samples = new float[20];
        samples[7] = 1;
        var data = new WaveformPeakReader(1, 4).Read(new TestSampleProvider(samples, 20, 1), CancellationToken.None);
        Assert.That(data.Peaks.Count, Is.LessThanOrEqualTo(4));
        Assert.That(data.Peaks.Max(), Is.EqualTo(1));
    }

    [Test]
    public void ObservesCancellation()
    {
        Assert.Throws<OperationCanceledException>(() => new WaveformPeakReader().Read(
            new TestSampleProvider([0f], 1, 1), new CancellationToken(true)));
    }
}
