using Hitbench.Resources.DataModels;
using Hitbench.Services;

namespace Hitbench.Test.Services;

public class OHsExportEnvelopeTests
{
    [Test]
    public void MixtureRetainsEachComponentsEnvelopeAndFullDuration()
    {
        var timeline = new ControlPointVolumeTimeline([new(0, 0, IsRamp: true), new(1000, 100)]);
        using var a = DecodedAudio.CopyFrom(Enumerable.Repeat(.4f, 88200).ToArray());
        using var b = DecodedAudio.CopyFrom(Enumerable.Repeat(.2f, 44100).ToArray());
        var asset = OHsExportAsset.CreateShaped([new("a.wav", 100, OHsExportEnvelope.Create(timeline, 0, 44100)),
            new("b.wav", 100, OHsExportEnvelope.Create(timeline, 22050, 22050))]);
        using var output = OHsExportAudio.Render(asset, file => file == "a.wav" ? a : b, default);
        Assert.That(output.FrameCount, Is.EqualTo(44100));
        Assert.That(output.Samples[22050], Is.EqualTo(.25f).Within(1e-6));
        Assert.That(output.Samples[66150], Is.EqualTo(.3f).Within(1e-6));
    }
    [Test]
    public void SourceMultiplicityAndEquivalentScalarHaveSameRecipe()
    {
        var curve = OHsExportEnvelope.Create(new([new(0, 10, IsRamp: true), new(1000, 50)]), 0, 44100);
        Assert.That(OHsExportAsset.CreateShaped([new("a.wav", 100, curve), new("a.wav", 100, curve)]).Id,
            Is.EqualTo(OHsExportAsset.CreateShaped([new("a.wav", 200, curve)]).Id));
    }
    [Test]
    public void TranslationAndCollinearBoundariesShareCanonicalRecipe()
    {
        var a = new ControlPointVolumeTimeline([new(0, 10, IsRamp: true), new(2000, 50)]);
        var b = new ControlPointVolumeTimeline([new(1000, 10, IsRamp: true), new(2000, 30, IsRamp: true), new(3000, 50)]);
        var x = OHsExportEnvelope.Create(a, 0, 88200);
        var y = OHsExportEnvelope.Create(b, 44100, 88200);
        Assert.That(x.Key, Is.EqualTo(y.Key));
        Assert.That(x.GetGain(44100), Is.EqualTo(.3).Within(1e-9));
        Assert.That(OHsExportAsset.CreateShaped([new("a.wav", 100, x)]).Id,
            Is.EqualTo(OHsExportAsset.CreateShaped([new("A.wav", 50, y.Scale(2, 1))]).Id));
    }

    [Test]
    public void FixedStepTailAndDifferentLengthComponentsRenderIndividually()
    {
        var timeline = new ControlPointVolumeTimeline([new(0, 0, IsRamp: true), new(1000, 100), new(1500, 20)]);
        var curve = OHsExportEnvelope.Create(timeline, 0, 88200);
        using var source = DecodedAudio.CopyFrom(Enumerable.Repeat(.4f, 176400).ToArray());
        using var rendered = OHsExportAudio.Render(OHsExportAsset.CreateShaped([new("a.wav", 100, curve)]), _ => source, default);
        Assert.That(rendered.FrameCount, Is.EqualTo(88200));
        Assert.That(rendered.Samples[0], Is.Zero);
        Assert.That(rendered.Samples[44100], Is.EqualTo(.2f).Within(1e-6));
        Assert.That(rendered.Samples[132300], Is.EqualTo(.08f).Within(1e-6));
        Assert.That(curve.IsConstant, Is.False);
    }
}
