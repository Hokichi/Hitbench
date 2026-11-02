using Hitbench.Resources.DataModels;
using Hitbench.Services;

namespace Hitbench.Test.Services;

public class OHsExportRecipeTests
{
    [Test]
    public void ClippingProofChecksStereoGainAndFullTail()
    {
        foreach (var (left, right, gain, expected) in new[]
        {
            (0f, .8f, 200, false), (0f, -.8f, 200, false),
            (1f, -1f, 100, true), (.5f, -.5f, 200, true), (.2f, -.3f, 70, true)
        })
        {
            using var audio = DecodedAudio.CopyFrom([left, right]);
            Assert.That(OHsExportAudio.CanMixWithoutClipping([OHsExportAsset.Create([("a.wav", gain)])], _ => audio, default),
                Is.EqualTo(expected), $"stereo ({left}, {right}), gain {gain}%");
        }
        using var a = DecodedAudio.CopyFrom([.8f, -.8f, .8f, -.8f]);
        using var b = DecodedAudio.CopyFrom([.7f, -.7f]);
        using var c = DecodedAudio.CopyFrom([-.8f, .8f]);
        var cancelling = OHsExportAsset.Create([("a.wav", 100), ("b.wav", 100), ("c.wav", 100)]);
        Assert.That(OHsExportAudio.CanMixWithoutClipping([cancelling], file => file == "a.wav" ? a : file == "b.wav" ? b : c, default), Is.True,
            "Assess the final mixture; intermediate component sums must not be clamped.");
        var tail = OHsExportAsset.Create([("a.wav", 200), ("c.wav", 100)]);
        Assert.That(OHsExportAudio.CanMixWithoutClipping([tail], file => file == "a.wav" ? a : c, default), Is.False,
            "The short cancelling component cannot conceal the longer source's clipping tail.");
        using var full = DecodedAudio.CopyFrom([1f, -1f]);
        var repeated = OHsExportAsset.Create([("a.wav", 70), ("A.wav", 40)]);
        Assert.That(OHsExportAudio.CanMixWithoutClipping([repeated], _ => full, default), Is.False);
        Assert.That(OHsExportAudio.CanMixWithoutClipping([OHsExportAsset.Create([("zero.wav", 0)])],
            _ => throw new AssertionException("Silence has no PCM sources."), default), Is.True);
        Assert.That(OHsExportAudio.CanMixWithoutClipping([], _ => throw new AssertionException("No assets."), default), Is.True);
    }

    [Test]
    public void ClippingProofChecksEachAssetOnce()
    {
        using var audio = DecodedAudio.CopyFrom([.2f, -.2f]);
        var asset = OHsExportAsset.Create([("a.wav", 100)]);
        var calls = 0;
        Assert.That(OHsExportAudio.CanMixWithoutClipping(Enumerable.Repeat(asset, 3000), _ => { calls++; return audio; }, default), Is.True);
        Assert.That(calls, Is.EqualTo(1));
        calls = 0;
        Assert.That(OHsExportAudio.CanMixWithoutClipping([asset, OHsExportAsset.Create([("a.wav", 50)]), asset],
            _ => { calls++; return audio; }, default), Is.True);
        Assert.That(calls, Is.EqualTo(2), "Different gain recipes each require their own proof.");
    }

    [TestCase(float.NaN, 100), TestCase(float.PositiveInfinity, 100), TestCase(float.NegativeInfinity, 100)]
    [TestCase(.2f, -1), TestCase(float.MaxValue, 200)]
    public void ClippingProofRejectsInvalidAudioAndGain(float value, int gain)
    {
        using var audio = DecodedAudio.CopyFrom([value, value]);
        Assert.Throws<InvalidDataException>(() => OHsExportAudio.CanMixWithoutClipping([OHsExportAsset.Create([("a.wav", gain)])], _ => audio, default));
    }

    [Test]
    public void ClippingProofRejectsEmptyDecodedSource()
    {
        using var audio = DecodedAudio.CopyFrom([]);
        Assert.Throws<InvalidDataException>(() => OHsExportAudio.CanMixWithoutClipping([OHsExportAsset.Create([("a.wav", 100)])], _ => audio, default));
    }

    [TestCase(false), TestCase(true)]
    public void ClippingProofHonorsCancellationWithoutDisposingSources(bool duringResolve)
    {
        using var audio = DecodedAudio.CopyFrom(new float[32768]);
        using var cancellation = new CancellationTokenSource();
        if (!duringResolve) cancellation.Cancel();
        var calls = 0;
        Assert.Catch<OperationCanceledException>(() => OHsExportAudio.CanMixWithoutClipping([OHsExportAsset.Create([("a.wav", 100)])], _ =>
        {
            calls++; cancellation.Cancel(); return audio;
        }, cancellation.Token));
        Assert.That(calls, Is.EqualTo(duringResolve ? 1 : 0));
        Assert.That(audio.Samples.Length, Is.EqualTo(32768));
        Assert.Catch<OperationCanceledException>(() => OHsExportAudio.CanMixWithoutClipping([], _ => audio, cancellation.Token));
    }

    [Test]
    public void ClippingProofDoesNotAllocateRenderedPcm()
    {
        var budget = new AudioMemoryBudget(maximumBytes: 882000L * sizeof(float));
        using var audio = DecodedAudio.CopyFrom(new float[882000], budget);
        var asset = OHsExportAsset.Create([("a.wav", 70)]);
        Func<string, DecodedAudio> resolve = _ => audio;
        OHsExportAudio.CanMixWithoutClipping([asset], resolve, default);
        var before = GC.GetAllocatedBytesForCurrentThread();
        var safe = OHsExportAudio.CanMixWithoutClipping([asset], resolve, default);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.That(safe, Is.True);
        Assert.That(allocated, Is.LessThan((long)audio.Samples.Length * sizeof(float)));
        Assert.That(audio.Samples.Length, Is.EqualTo(882000));
        Assert.That(budget.UsedBytes, Is.EqualTo(882000L * sizeof(float)));
        Assert.That(budget.PeakBytes, Is.EqualTo(budget.UsedBytes), "Proof must not reserve a second PCM buffer.");
    }

    [Test]
    public void MixtureRetainsComponentGainsAndFullDuration()
    {
        using var a = DecodedAudio.CopyFrom(Enumerable.Repeat(.4f, 88200).ToArray());
        using var b = DecodedAudio.CopyFrom(Enumerable.Repeat(.2f, 44100).ToArray());
        var asset = OHsExportAsset.Create([("a.wav", 70), ("b.wav", 50)]);
        using var output = OHsExportAudio.Render(asset, file => file == "a.wav" ? a : b, default);
        Assert.That(output.FrameCount, Is.EqualTo(44100));
        Assert.That(output.Samples[22050], Is.EqualTo(.38f).Within(1e-6));
        Assert.That(output.Samples[66150], Is.EqualTo(.28f).Within(1e-6));
    }

    [Test]
    public void SourceMultiplicityAndCaseShareCanonicalRecipe()
    {
        Assert.That(OHsExportAsset.Create([("a.wav", 100), ("A.wav", 100)]).Id,
            Is.EqualTo(OHsExportAsset.Create([("A.wav", 200)]).Id));
        Assert.That(OHsExportAsset.Create([("a.wav", 0)]).Id,
            Is.EqualTo(OHsExportAsset.Create([("b.wav", 0)]).Id));
    }

    [Test]
    public void RawMixMatchesPerSampleReferenceWithStereoClippingAndDifferentLengths()
    {
        var random = new Random(512);
        for (var fixture = 0; fixture < 32; fixture++)
        {
            using var a = DecodedAudio.CopyFrom(Enumerable.Range(0, 240).Select(_ => (float)(random.NextDouble() * 2 - 1)).ToArray());
            using var b = DecodedAudio.CopyFrom(Enumerable.Range(0, 100).Select(_ => (float)(random.NextDouble() * 2 - 1)).ToArray());
            var gain = fixture % 2 == 0 ? 200 : 30;
            using var output = OHsExportAudio.Render(OHsExportAsset.Create([("a.wav", gain), ("b.wav", 70)]),
                file => file == "a.wav" ? a : b, default);
            for (var i = 0; i < a.Samples.Length; i++)
            {
                var expected = Math.Clamp(a.Samples[i] * gain / 100d + (i < b.Samples.Length ? b.Samples[i] * .7 : 0), -1, 1);
                Assert.That(output.Samples[i], Is.EqualTo(expected).Within(1e-6));
            }
        }
    }

    [TestCase(false), TestCase(true)]
    public void CancellationDoesNotDisposeBorrowedAudio(bool duringResolve)
    {
        using var audio = DecodedAudio.CopyFrom(new float[32768]);
        using var cancellation = new CancellationTokenSource();
        if (!duringResolve) cancellation.Cancel();
        var calls = 0;
        Assert.Catch<OperationCanceledException>(() => OHsExportAudio.Render(OHsExportAsset.Create([("a.wav", 100)]), _ =>
        {
            calls++;
            cancellation.Cancel();
            return audio;
        }, cancellation.Token));
        Assert.That(calls, Is.EqualTo(duringResolve ? 1 : 0));
        Assert.That(audio.Samples.Length, Is.EqualTo(32768));
    }
}
