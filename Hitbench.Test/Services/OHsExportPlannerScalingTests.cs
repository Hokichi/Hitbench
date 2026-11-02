using Hitbench.Core.Enums;
using Hitbench.Resources.DataModels;
using Hitbench.Services;

namespace Hitbench.Test.Services;

public class OHsExportPlannerScalingTests
{
    [TestCase(false), TestCase(true)]
    public void DecodedRepeatedArrangementPreparesOneRecipe(bool ramp)
    {
        using var audio = DecodedAudio.CopyFrom(Enumerable.Repeat(.2f, 88200).ToArray());
        var input = OHsExportControlPointSeparationTests.Input(ramp
            ? [new() { Volume = 10, IsRamp = true }, new() { Timestamp = 10000, Volume = 80 }]
            : [new() { Volume = 40 }], Enumerable.Range(0, 3000).Select(i => i * 100).ToArray(), gain: 70);
        var resolves = 0;
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var watch = System.Diagnostics.Stopwatch.StartNew();
        var plan = OHsExportPlanner.Plan(input, cancellation.Token, resolve: _ => { resolves++; return audio; });
        TestContext.Out.WriteLine($"3,000 decoded hits, ramp={ramp}: {watch.Elapsed.TotalMilliseconds:F0} ms, {resolves} source inspections");
        Assert.That(plan.Circles, Has.Count.EqualTo(3000));
        Assert.That(plan.AudioFileCount, Is.EqualTo(1));
        Assert.That(resolves, Is.EqualTo(1));
        using var rendered = OHsExportAudio.Render(plan.Banks.Values.Single().Values.Single(), _ => audio, default);
        Assert.That(rendered.Samples[0], Is.EqualTo(.14f).Within(1e-6));
        Assert.That(audio.FrameCount, Is.EqualTo(44100));
    }

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
        Assert.That(plan.AudioFileCount, Is.EqualTo(1));
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
    [TestCase(SampleType.Normal, 7)] [TestCase(SampleType.Clap, 14)]
    public void HundredsOfConflictingRepeatedRecipesFinishAtProvenMinimum(SampleType type, int files)
    {
        var map = OHsExportBeatmap.Parse("[TimingPoints]\n0,500,4,1,1,100,1,0\n");
        var voices = Enumerable.Range(0, 500).Select(i => new OHsExportVoice(1000 + i * 100,
            $"sample{i % 7}.wav", 70, "Normal", type, 0)).ToArray();
        var input = new OHsExportInput(new("Hitsounds", 1, ".ogg", ".ogg", false), map, voices, map.TimingPoints);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var plan = OHsExportPlanner.Plan(input, cancellation.Token);
        Assert.That(plan.Circles.Count, Is.EqualTo(500));
        Assert.That(plan.AdditionalPointCount, Is.EqualTo(499));
        Assert.That(plan.Banks.Count, Is.EqualTo(7));
        Assert.That(plan.AudioFileCount, Is.EqualTo(files));
    }

    [TestCase(14)] [TestCase(1400)]
    public void MixedRepeatedRecipesFinishAtFileMinimum(int groups)
    {
        var map = OHsExportBeatmap.Parse("[TimingPoints]\n0,500,4,1,1,100,1,0\n");
        var input = new OHsExportInput(new("Hitsounds", 1, ".wav", ".wav", false), map, MixedExportVoices.Create(groups), map.TimingPoints);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var plan = OHsExportPlanner.Plan(input, cancellation.Token);
        Assert.That(plan.AudioFileCount, Is.EqualTo(12));
        Assert.That(plan.Circles, Has.Count.EqualTo(groups));
    }
}
