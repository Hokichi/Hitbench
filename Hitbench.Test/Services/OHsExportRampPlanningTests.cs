using Hitbench.Core.Entities;
using Hitbench.Core.Enums;
using Hitbench.Resources.DataModels;
using Hitbench.Services;

namespace Hitbench.Test.Services;

public class OHsExportRampPlanningTests
{
    [Test]
    public void ZeroControlVolumeDoesNotReplaceAudibleNormalWithSilentSupport()
    {
        using var audio = DecodedAudio.CopyFrom(new[] { .2f, .2f });
        var baseline = Input([new(0, 0), new(1000, 100)], []);
        var input = new OHsExportInput(baseline.Options with { StartingIndex = 999 }, baseline.Beatmap,
            [new(0, "a.wav", 100, "Normal", SampleType.Normal, 0), new(1100, "b.wav", 100, "Soft", SampleType.Clap, 1)],
            baseline.RequiredPoints, baseline.ControlPoints);
        Assert.Throws<InvalidOperationException>(() => OHsExportPlanner.Plan(input, default));
        input = new(input.Options with { StartingIndex = 998 }, input.Beatmap, input.Voices, input.RequiredPoints, input.ControlPoints);
        var plan = OHsExportPlanner.Plan(input, default);
        Assert.That(plan.AudioFileCount, Is.EqualTo(2));
        Assert.That(plan.Banks.Keys, Is.EqualTo(new[] { 998, 999 }));
    }
    [Test]
    public void LongFixedRunUsesBoundedSearchStack()
    {
        using var audio = DecodedAudio.CopyFrom(new[] { .2f, .2f });
        var input = Input([new(0, 40)], Enumerable.Range(0, 5000).Select(i => i * 20).ToArray());
        var plan = OHsExportPlanner.Plan(input, default);
        Assert.That(plan.Circles, Has.Count.EqualTo(5000));
        Assert.That((plan.AudioFileCount, plan.AdditionalPointCount), Is.EqualTo((1, 0)));
    }
    [Test]
    public void FixedRegionsKeepOneSourceRecipeWithNativeGainReuse()
    {
        using var audio = DecodedAudio.CopyFrom(Enumerable.Repeat(.2f, 882).ToArray());
        var input = Input([new(0, 10), new(1000, 50)], [100, 1100]);
        var plan = OHsExportPlanner.Plan(input, default);
        Assert.That(plan.AudioFileCount, Is.EqualTo(1));
        var rows = OHsExportPlaybackOracle.Resolve(OHsExportBeatmapWriter.Write(input, plan));
        var native = rows.Select(r => r.Volume).ToArray();
        Assert.That(native[1], Is.EqualTo(native[0] * 5));
        using var rendered = OHsExportAudio.Render(plan.Banks.Values.Single().Values.Single(), _ => audio, default);
        Assert.That(rendered.Samples[0] * native[0] / 100, Is.EqualTo(.02f).Within(1e-6));
    }

    [Test]
    public void RampStartUsesRoundedLookupVolumeWithoutShapingTail()
    {
        using var audio = DecodedAudio.CopyFrom(Enumerable.Repeat(.2f, 88200).ToArray());
        var input = Input([new(0, 0, IsRamp: true), new(1000, 100)], [0]);
        var plan = OHsExportPlanner.Plan(input, default);
        var hit = OHsExportPlaybackOracle.Resolve(OHsExportBeatmapWriter.Write(input, plan)).Single();
        Assert.That(hit.Volume, Is.EqualTo(1));
        using var rendered = OHsExportAudio.Render(plan.Banks.Values.Single().Values.Single(), _ => audio, default);
        Assert.That(rendered.Samples[44100] * hit.Volume / 100, Is.EqualTo(.002f).Within(1e-6));
    }

    internal static OHsExportInput Input(ControlPointVolumeSnapshot[] controls, int[] times)
    {
        return OHsExportControlPointSeparationTests.Input(controls.Select(p => new ControlPoint
            { Timestamp = p.Timestamp, Volume = p.Volume, IsDisabled = p.IsDisabled, IsRamp = p.IsRamp }).ToArray(), times);
    }
}
