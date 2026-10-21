using osuHitsounder.Core.Enums;
using osuHitsounder.Resources.DataModels;
using osuHitsounder.Services;

namespace osuHitsounder.Test.Services;

public class OHsExportPlannerScalingTests
{
    [Test]
    public void HomogeneousArrangementUsesOneBankWithoutEnumeratingAllIntervals()
    {
        var map = OHsExportBeatmap.Parse("[TimingPoints]\n0,500,4,1,1,100,1,0\n");
        var voices = Enumerable.Range(0, 10000).Select(i => new OHsExportVoice(1000 + i * 100,
            "same.wav", 70, "Normal", SampleType.Normal, 0)).ToArray();
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var plan = OHsExportPlanner.Plan(new(new("Hitsounds", 999, ".ogg", ".ogg", false), map, voices, map.TimingPoints), cancellation.Token);
        Assert.That(plan.AdditionalPointCount, Is.Zero);
        Assert.That(plan.Banks.Keys, Is.EqualTo(new[] { 999 }));
    }

    [Test]
    public void RecipeCacheSharesImmutableAssetsWithinOneAttempt()
    {
        var assets = new Dictionary<string, OHsExportAsset>();
        var voice = new OHsExportVoice(1000, "same.wav", 70, "Normal", SampleType.Normal, 0);
        var first = OHsExportRepresentations.Build([voice], default, assets);
        var next = OHsExportRepresentations.Build([voice with { Timestamp = 2000 }], default, assets);
        Assert.That(next[0].Slots.Values.Single(), Is.SameAs(first[0].Slots.Values.Single()));
    }
    [Test]
    public void HundredsOfConflictingRepeatedRecipesFinishAtProvenMinimum()
    {
        var map = OHsExportBeatmap.Parse("[TimingPoints]\n0,500,4,1,1,100,1,0\n");
        var voices = Enumerable.Range(0, 500).Select(i => new OHsExportVoice(1000 + i * 100,
            $"sample{i % 7}.wav", 70, "Normal", SampleType.Normal, 0)).ToArray();
        var input = new OHsExportInput(new("Hitsounds", 1, ".ogg", ".ogg", false), map, voices, map.TimingPoints);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var plan = OHsExportPlanner.Plan(input, cancellation.Token);
        Assert.That(plan.Circles.Count, Is.EqualTo(500));
        Assert.That(plan.AdditionalPointCount, Is.EqualTo(499));
        Assert.That(plan.Banks.Count, Is.EqualTo(7));
    }
}
