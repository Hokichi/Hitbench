using Hitbench.Core.Enums;
using Hitbench.Resources.DataModels;
using Hitbench.Services;

namespace Hitbench.Test.Services;

public class OHsExportPlannerScalingTests
{
    [TestCase(false), TestCase(true)]
    public void RepeatedArrangementPreparesOneRecipe(bool ramp)
    {
        using var audio = DecodedAudio.CopyFrom(Enumerable.Repeat(.2f, 88200).ToArray());
        var input = OHsExportControlPointSeparationTests.Input(ramp
            ? [new() { Volume = 10, IsRamp = true }, new() { Timestamp = 10000, Volume = 80 }]
            : [new() { Volume = 40 }], Enumerable.Range(0, 3000).Select(i => i * 100).ToArray(), gain: 70);

        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var watch = System.Diagnostics.Stopwatch.StartNew();
        var plan = OHsExportPlanner.Plan(input, cancellation.Token);
        TestContext.Out.WriteLine($"3,000 decoded hits, ramp={ramp}: {watch.Elapsed.TotalMilliseconds:F0} ms, audio-independent planning");
        Assert.That(plan.Circles, Has.Count.EqualTo(3000));
        Assert.That(plan.AudioFileCount, Is.EqualTo(1));

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
    [TestCase(SampleType.Normal, 7)] [TestCase(SampleType.Clap, 7)]
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
        Assert.That(plan.AudioFileCount, Is.EqualTo(10));
        Assert.That(plan.Circles, Has.Count.EqualTo(groups));
    }
    [TestCase(4, false), TestCase(12, false), TestCase(4, true), TestCase(8, true)]
    public void RampAndPhysicalCopyLayoutsFinish(int count, bool clap)
    {
        using var audio = DecodedAudio.CopyFrom(Enumerable.Repeat(.2f, 882).ToArray());
        var baseline = OHsExportRampPlanningTests.Input([], []);
        var voices = Enumerable.Range(0, count).SelectMany(i => clap
            ? new[] { new OHsExportVoice(100 + i * 100, $"n{i}.wav", 100, "Normal", SampleType.Normal, 0), new OHsExportVoice(100 + i * 100, "clap.wav", 100, "Soft", SampleType.Clap, 1) }
            : new[] { new OHsExportVoice(100 + i * 100, "a.wav", 100, "Normal", SampleType.Normal, 0) }).ToArray();
        var input = new OHsExportInput(baseline.Options, baseline.Beatmap, voices, baseline.RequiredPoints,
            clap ? [] : [new(0, 10, IsRamp: true), new(10000, 80)]);
        using var cancel = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var clock = System.Diagnostics.Stopwatch.StartNew();
        var plan = OHsExportPlanner.Plan(input, cancel.Token);
        TestContext.Out.WriteLine($"{count}, clap={clap}: {clock.Elapsed.TotalMilliseconds:F0}ms");
        Assert.That(plan.AudioFileCount, Is.EqualTo(clap ? count * 2 : 1));
        Assert.That(plan.AdditionalPointCount, Is.EqualTo(clap ? count - 1 : 0));
    }

    [Test]
    public void NonlinearGroupWithLongQuietRunPreservesPlayback()
    {
        using var loud = DecodedAudio.CopyFrom(Enumerable.Repeat(.999f, 4000).ToArray());
        using var quiet = DecodedAudio.CopyFrom(Enumerable.Repeat(.02f, 4000).ToArray());
        var baseline = OHsExportRampPlanningTests.Input([], []);
        var voices = Enumerable.Range(0, 500).Select(i => new OHsExportVoice(i * 20, i == 0 ? "loud.wav" : "quiet.wav", 100, "Normal", SampleType.Normal, 0)).ToArray();
        var input = new OHsExportInput(baseline.Options, baseline.Beatmap, voices, baseline.RequiredPoints);
        using var cancel = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        DecodedAudio Resolve(string file) => file == "loud.wav" ? loud : quiet;
        var plan = OHsExportPlanner.Plan(input, cancel.Token);

        Assert.That(plan.AudioFileCount, Is.EqualTo(2));
        var text = OHsExportBeatmapWriter.Write(input, plan);
        var pcm = OHsExportPlaybackOracle.Render(text, name =>
            OHsExportAudio.Render(plan.Banks.SelectMany(b => b.Value.Select(s => (Name: OHsExportRepresentations.FileName(s.Key, b.Key, ".wav"), Asset: s.Value))).Single(p => p.Name == name).Asset, Resolve, default), 4000);
        Assert.That(pcm[0], Is.EqualTo(.999f).Within(1e-6));
        Assert.That(pcm[3000 * 2], Is.EqualTo(.04f).Within(1e-6));
        Assert.That(loud.FrameCount, Is.EqualTo(2000));
    }

}
