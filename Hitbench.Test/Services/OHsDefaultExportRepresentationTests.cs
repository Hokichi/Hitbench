using Hitbench.Core.Entities;
using Hitbench.Core.Enums;
using Hitbench.Resources.DataModels;
using Hitbench.Services;

namespace Hitbench.Test.Services;

public class OHsDefaultExportRepresentationTests
{
    [Test]
    public void NativeRepresentationHasNoAssetsAndMixedCannotBeNative()
    {
        var voice = new OHsExportVoice(100, "SkinDefault/normal-hitnormal.wav", 100, "Normal", SampleType.Normal, 0, IsDefault: true);
        var native = OHsExportRepresentations.TryBuildNative([voice], 100);
        Assert.That(native, Is.Not.Null); Assert.That(native!.IsNative, Is.True); Assert.That(native.Slots, Is.Empty);
        Assert.That(OHsExportRepresentations.TryBuildNative([voice, voice with { IsDefault = false }], 100), Is.Null);
        Assert.That(OHsExportRepresentations.TryBuildNative([voice, voice], 100), Is.Null);
    }

    [Test]
    public void DefaultVoiceResolvesExactCachedSourceAndDisabledSoloDoesNotSuppressCustom()
    {
        var track = new Track { UsesDefaultSample = true, IsTrackSoloed = true, Addition = "Drum", SampleType = SampleType.Clap, Samples = [new() { Timestamp = 100 }] };
        var project = new ProjectData { AllowDefaults = true, Tracks = [track, new() { Samples = [new() { FileName = "a.wav", Timestamp = 200 }] }] };
        var map = OHsExportBeatmap.Parse("[TimingPoints]\n0,500,4,1,1,100,1,0\n");
        var options = new OHsExportOptions("Hitsounds", 1, ".ogg", ".ogg", false);
        var input = OHsExportInputFactory.Create(project, map, options, default, ["SkinDefault/drum-hitclap.wav"]);
        Assert.That(input.Voices.Single().IsDefault, Is.True);
        Assert.That(input.Voices.Single().FileName, Is.EqualTo("SkinDefault/drum-hitclap.wav"));
        project.AllowDefaults = false;
        input = OHsExportInputFactory.Create(project, map, options, default, ["SkinDefault/drum-hitclap.wav"]);
        Assert.That(input.Voices.Single().FileName, Is.EqualTo("a.wav"));
        Assert.That(input.Voices.Single().IsDefault, Is.False);
    }
}
