using Hitbench.Core.Enums;
using Hitbench.Resources.DataModels;
using Hitbench.Services;

namespace Hitbench.Test.Services;

public class OHsDefaultExportPlannerTests
{
    private static OHsExportVoice Voice(int time, bool native) => new(time, native ? "SkinDefault/normal-hitnormal.wav" : "a.wav", 100, "Normal", SampleType.Normal, 0, native);
    private static OHsExportInput Input(params OHsExportVoice[] voices)
    {
        var map = OHsExportBeatmap.Parse("[TimingPoints]\n0,500,4,1,1,100,1,0\n");
        return new(new("Hitsounds", 1, ".ogg", ".ogg", false), map, voices, map.TimingPoints);
    }
    [Test]
    public void AllNativeGroupsUseBankZeroAndGenerateNoAssets()
    {
        var input = Input(Voice(100, true), Voice(200, true));
        var plan = OHsExportPlanner.Plan(input, default);
        Assert.That(plan.AudioFileCount, Is.Zero); Assert.That(plan.Banks, Is.Empty);
        Assert.That(plan.Circles.Select(c => c.SampleIndex), Is.EqualTo(new[] { 0, 0 }));
        Assert.That(plan.TimingPoints.All(p => p.SampleIndex == 0), Is.True);
        Assert.That(OHsExportBeatmapWriter.Write(input, plan), Does.Contain("0,500,4,1,0,100,1,0"));
    }
    [TestCase(200)]
    [TestCase(102)]
    public void NativeCustomNativeTransitionsPreserveOneObjectAndSources(int middle)
    {
        var input = Input(Voice(100, true), Voice(middle, true), Voice(middle, false), Voice(300, true));
        var plan = OHsExportPlanner.Plan(input, default);
        Assert.That(plan.Circles.Select(c => c.Timestamp), Is.EqualTo(new[] { 100, middle, 300 }));
        Assert.That(plan.Circles.Select(c => c.SampleIndex), Is.EqualTo(new[] { 0, 1, 0 }));
        foreach (var circle in plan.Circles)
            Assert.That(plan.TimingPoints.Last(p => p.Timestamp <= circle.Timestamp + 5).SampleIndex, Is.EqualTo(circle.SampleIndex));
        Assert.That(plan.Banks[1].Values.SelectMany(a => a.Sources).Select(s => s.FileName), Is.EquivalentTo(new[] { "SkinDefault/normal-hitnormal.wav", "a.wav" }));
    }
}
