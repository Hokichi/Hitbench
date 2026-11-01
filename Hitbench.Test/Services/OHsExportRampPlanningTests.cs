using Hitbench.Core.Entities;
using Hitbench.Core.Enums;
using Hitbench.Resources.DataModels;
using Hitbench.Services;

namespace Hitbench.Test.Services;

public class OHsExportRampPlanningTests
{
    [Test]
    public void FixedRegionsKeepOneSourceRecipeWithNativeGainReuse()
    {
        using var audio = DecodedAudio.CopyFrom(Enumerable.Repeat(.2f, 882).ToArray());
        var input = Input([new(0, 10), new(1000, 50)], [100, 1100]);
        var plan = OHsExportPlanner.Plan(input, default, resolve: _ => audio);
        Assert.That(plan.AudioFileCount, Is.EqualTo(1));
        var rows = OHsExportPlaybackOracle.Resolve(OHsExportBeatmapWriter.Write(input, plan));
        var native = rows.Select(r => r.Volume).ToArray();
        Assert.That(native[1], Is.EqualTo(native[0] * 5));
        using var rendered = OHsExportAudio.Render(plan.Banks.Values.Single().Values.Single(), _ => audio, default);
        Assert.That(rendered.Samples[0] * native[0] / 100, Is.EqualTo(.02f).Within(1e-6));
    }

    [Test]
    public void SilentStartAudibleTailDoesNotSelectZeroNativeFactor()
    {
        using var audio = DecodedAudio.CopyFrom(Enumerable.Repeat(.2f, 88200).ToArray());
        var input = Input([new(0, 0, IsRamp: true), new(1000, 100)], [0]);
        var plan = OHsExportPlanner.Plan(input, default, resolve: _ => audio);
        var hit = OHsExportPlaybackOracle.Resolve(OHsExportBeatmapWriter.Write(input, plan)).Single();
        Assert.That(hit.Volume, Is.GreaterThan(0));
        using var rendered = OHsExportAudio.Render(plan.Banks.Values.Single().Values.Single(), _ => audio, default);
        Assert.That(rendered.Samples[44100] * hit.Volume / 100, Is.EqualTo(.1f).Within(1e-6));
    }

    internal static OHsExportInput Input(ControlPointVolumeSnapshot[] controls, int[] times)
    {
        var map = OHsExportBeatmap.Parse("[TimingPoints]\n0,500,4,1,1,100,1,0\n");
        return new(new("Hitsounds", 1, ".wav", ".wav", false), map,
            times.Select(t => new OHsExportVoice(t, "a.wav", 100, "Normal", SampleType.Normal, 0)).ToArray(), map.TimingPoints, controls);
    }
}
