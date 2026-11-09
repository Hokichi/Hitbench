using Hitbench.Core.Enums;
using Hitbench.Resources.DataModels;
using Hitbench.Services;

namespace Hitbench.Test.Services;

public class OHsHitNormalExportPlanningTests
{
    private static OHsExportInput Input(string set, OHsHitNormalConfiguration config, params OHsExportVoice[] voices)
    {
        var map = OHsExportBeatmap.Parse("[TimingPoints]\n0,500,4," + (set == "Normal" ? 1 : set == "Soft" ? 2 : 3) + ",1,100,1,0\n");
        return new(new("Hitsounds", 1, ".wav", ".wav", false), map, voices, map.TimingPoints, hitNormals: config);
    }

    [TestCase("Normal")][TestCase("Soft")][TestCase("Drum")]
    public void DefaultFallbackRequiresNativeLookupWithoutSilence(string set)
    {
        var plan = OHsExportPlanner.Plan(Input(set, new(), new OHsExportVoice(100, "clap.wav", 100, "Drum", SampleType.Clap, 0)), default);
        Assert.That(plan.Circles.Single().NormalSet, Is.EqualTo(set));
        Assert.That(plan.AudioFileCount, Is.EqualTo(1));
        Assert.That(plan.Banks.Values.Single()[new(set, SampleType.Normal)].IsNativeFallback, Is.True);
    }

    [TestCase("Normal")][TestCase("Soft")][TestCase("Drum")]
    public void CustomFallbackUsesConfiguredSourceAndExplicitNormalOverridesIt(string set)
    {
        var config = new OHsHitNormalConfiguration("n.wav", "s.wav", "d.wav");
        var plan = OHsExportPlanner.Plan(Input(set, config, new OHsExportVoice(100, "clap.wav", 100, "Drum", SampleType.Clap, 0)), default);
        Assert.That(plan.Banks.Values.Single()[new(set, SampleType.Normal)].Sources, Is.EqualTo(new[] { (config.GetPath(set), 100) }));
        var authored = OHsExportPlanner.Plan(Input(set, config, new OHsExportVoice(100, "normal.wav", 0, set, SampleType.Normal, 0)), default);
        Assert.That(authored.Banks.Values.Single()[new(set, SampleType.Normal)].IsNativeFallback, Is.False);
        Assert.That(authored.Banks.Values.Single()[new(set, SampleType.Normal)].Sources, Is.Empty);
    }

    [Test]
    public void NativeFallbackCannotShareCustomNormalSlot()
    {
        var plan = OHsExportPlanner.Plan(Input("Normal", new(),
            new OHsExportVoice(100, "normal.wav", 100, "Normal", SampleType.Normal, 0),
            new OHsExportVoice(200, "clap.wav", 100, "Normal", SampleType.Clap, 1)), default);
        Assert.That(plan.Circles[0].SampleIndex, Is.Not.EqualTo(plan.Circles[1].SampleIndex));
        Assert.That(plan.AudioFileCount, Is.EqualTo(2));
    }

    [Test]
    public void EmptyArrangementCreatesNoFilesOrEvents()
    {
        var plan = OHsExportPlanner.Plan(Input("Normal", new("missing.wav")), default);
        Assert.That(plan.AudioFileCount, Is.Zero);
        Assert.That(plan.Circles, Is.Empty);
    }

    [Test]
    public void SameVoicesAcrossSamplesetChangeDoNotReuseWrongRecipe()
    {
        var original = Input("Normal", new("n.wav", "s.wav"),
            new OHsExportVoice(100, "clap.wav", 100, "Drum", SampleType.Clap, 0),
            new OHsExportVoice(200, "clap.wav", 100, "Drum", SampleType.Clap, 0));
        var input = new OHsExportInput(original.Options, original.Beatmap, original.Voices,
            original.RequiredPoints.Concat([new OHsExportTimingPoint(150, "-100", 4, 2, 1, 100, 0, OHsExportTimingKind.ProjectControl)]).ToArray(),
            hitNormals: original.HitNormals);
        var plan = OHsExportPlanner.Plan(input, default);
        Assert.That(plan.Circles.Select(c => c.NormalSet), Is.EqualTo(new[] { "Normal", "Soft" }));
        foreach (var circle in plan.Circles)
            Assert.That(plan.Banks[circle.SampleIndex][new(circle.NormalSet, SampleType.Normal)].Sources.Single().FileName,
                Is.EqualTo(circle.NormalSet == "Normal" ? "n.wav" : "s.wav"));
    }
}
