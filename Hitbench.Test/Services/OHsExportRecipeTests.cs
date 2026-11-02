using Hitbench.Resources.DataModels;
using Hitbench.Services;

namespace Hitbench.Test.Services;

public class OHsExportRecipeTests
{
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
