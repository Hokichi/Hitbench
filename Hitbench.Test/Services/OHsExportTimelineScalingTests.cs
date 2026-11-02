using Hitbench.Core.Entities;
using Hitbench.Core.Enums;
using Hitbench.Resources.DataModels;
using Hitbench.Services;

namespace Hitbench.Test.Services;

public class OHsExportTimelineScalingTests
{
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
        var plan = OHsExportPlanner.Plan(input, cancel.Token, resolve: _ => audio);
        TestContext.Out.WriteLine($"{count}, clap={clap}: {clock.Elapsed.TotalMilliseconds:F0}ms");
        Assert.That(plan.AudioFileCount, Is.EqualTo(clap ? count * 2 : 1));
        Assert.That(plan.AdditionalPointCount, Is.EqualTo(clap ? count - 1 : 0));
        Assert.That(OHsExportAudio.IsPlaybackEquivalent(input, plan, _ => audio, cancel.Token), Is.True);
    }

    [TestCase(12), TestCase(18)]
    public void EqualRankAlternativesAvoidRedundantValidation(int count)
    {
        var groups = Enumerable.Range(0, count).Select(i => (Time: 100 + i * 100,
            Choices: (IReadOnlyList<OHsExportRepresentation>)new[] { 49, 51 }.Select(v => new OHsExportRepresentation("Normal", "Normal", 1,
                new Dictionary<OHsExportSlot, OHsExportAsset> { [new("Normal", SampleType.Normal)] = OHsExportAsset.Create([($"s{i}.wav", 100 - v)]) }, 0, 0)).ToArray())).ToArray();
        using var cancel = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        int calls = 0;
        var required = new[] { new OHsExportTimingPoint(0, "500", 4, 1, 1, 100, 0, OHsExportTimingKind.SourceRed) };
        var plan = OHsExportLayoutSearch.FindTimeline(groups, required, 1, cancel.Token, null, (_, _) => { calls++; return true; });
        Assert.That((plan.AudioFileCount, plan.AdditionalPointCount), Is.EqualTo((count, count - 1)));
        Assert.That(calls, Is.LessThanOrEqualTo(2 * count + 1));
        for (var i = 0; i < count; i++)
            Assert.That(plan.Banks[i + 1].Values.Single().Id, Is.EqualTo(groups[i].Choices.Select(c => c.Slots.Values.Single().Id).Order(StringComparer.Ordinal).First()));
        var reversed = groups.Select(g => (g.Time, (IReadOnlyList<OHsExportRepresentation>)g.Choices.Reverse().ToArray())).ToArray();
        var retry = OHsExportLayoutSearch.FindTimeline(reversed, required, 1, cancel.Token, null);
        Assert.That(retry.Banks.Values.Select(b => b.Values.Single().Id), Is.EqualTo(plan.Banks.Values.Select(b => b.Values.Single().Id)));
        TestContext.Out.WriteLine($"{count} tied events: {calls} validations");
    }
    [Test]
    public void ProgressCanCancelWithoutAnImprovedIncumbent()
    {
        var choice = new OHsExportRepresentation("Normal", "Normal", 1,
            new Dictionary<OHsExportSlot, OHsExportAsset> { [new("Normal", SampleType.Normal)] = OHsExportAsset.Create([("a.wav", 100)]) }, 0, 0);
        var groups = new[] { (100, (IReadOnlyList<OHsExportRepresentation>)new[] { choice }) };
        using var cancel = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var reports = 0;
        var progress = new InlineProgress<OHsExportProgress>(_ => { reports++; cancel.Cancel(); });
        Assert.Catch<OperationCanceledException>(() => OHsExportLayoutSearch.FindTimeline(groups, [], 1, cancel.Token, progress,
            (_, _) => { Thread.Sleep(300); return true; }));
        Assert.That(reports, Is.GreaterThan(0));
        Assert.That(OHsExportLayoutSearch.FindTimeline(groups, [], 1, default, null).AudioFileCount, Is.EqualTo(1));
    }

    [Test]
    public void RejectedCandidateDoesNotBecomeIncumbent()
    {
        var choices = new[] { 50, 100 }.Select(v => new OHsExportRepresentation("Normal", "Normal", 1,
            new Dictionary<OHsExportSlot, OHsExportAsset> { [new("Normal", SampleType.Normal)] = OHsExportAsset.Create([("a.wav", v)]) }, 0, 0)).ToArray();
        var calls = 0;
        var plan = OHsExportLayoutSearch.FindTimeline([(100, choices)], [], 1, default, null, (p, _) => { calls++; return p.Banks.Values.Single().Values.Single().Sources.Single().GainPercent == 50; });
        Assert.That(calls, Is.GreaterThan(1));
        Assert.That(plan.Banks.Values.Single().Values.Single().Sources.Single().GainPercent, Is.EqualTo(50));
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
        var plan = OHsExportPlanner.Plan(input, cancel.Token, resolve: Resolve);
        Assert.That(OHsExportAudio.IsPlaybackEquivalent(input, plan, Resolve, cancel.Token), Is.True);
        Assert.That(plan.AudioFileCount, Is.EqualTo(2));
        Assert.That(loud.FrameCount, Is.EqualTo(2000));
    }

}
