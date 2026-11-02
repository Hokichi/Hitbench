using Hitbench.Core.Enums;
using Hitbench.Resources.DataModels;
using Hitbench.Services;

namespace Hitbench.Test.Services;

public class OHsExportSearchCancellationTests
{
    [TestCase(false), TestCase(true)]
    public void SearchCanAcceptFirstCandidateAfterFormerDeadline(bool timeline)
    {
        using var caller = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var delayed = false;
        void DelayOnce()
        {
            if (delayed) return;
            delayed = true;
            Thread.Sleep(5500);
        }
        var watch = System.Diagnostics.Stopwatch.StartNew();
        if (timeline)
        {
            var plan = OHsExportLayoutSearch.FindTimeline(Groups(), Required(), 1, caller.Token, null,
                (_, searchToken) => { DelayOnce(); searchToken.ThrowIfCancellationRequested(); return true; });
            Assert.That((plan.AudioFileCount, plan.AdditionalPointCount), Is.EqualTo((3, 1)));
            Assert.That(plan.Circles.Select(c => c.Timestamp), Is.EqualTo(new[] { 100, 200, 300, 400 }));
        }
        else
        {
            var progress = new InlineProgress<OHsExportProgress>(_ => DelayOnce());
            var layout = OHsExportLayoutSearch.Find(Recipes(), 2, caller.Token, progress);
            Assert.That(layout.Count, Is.EqualTo(2));
            Assert.That(layout.SelectMany(b => b.Values).SelectMany(a => a.Sources).Select(s => s.FileName),
                Is.EquivalentTo(new[] { "a.wav", "b.wav" }));
        }
        Assert.That(delayed, Is.True);
        Assert.That(watch.Elapsed, Is.GreaterThanOrEqualTo(TimeSpan.FromSeconds(5)));
    }

    [Test]
    public void ClippingRecipeStillRequiresExactAcceptance()
    {
        using var audio = DecodedAudio.CopyFrom(Enumerable.Repeat(.8f, 4000).ToArray());
        var baseline = OHsExportRampPlanningTests.Input([], [0, 20]);
        var input = new OHsExportInput(baseline.Options, baseline.Beatmap,
            baseline.Voices.Concat(baseline.Voices).ToArray(), baseline.RequiredPoints);
        using var caller = new CancellationTokenSource();
        var resolves = 0;
        var error = Assert.Catch<OperationCanceledException>(() => OHsExportPlanner.Plan(input, caller.Token, resolve: _ =>
        {
            if (++resolves == 2) caller.Cancel();
            return audio;
        }));
        Assert.That(error.CancellationToken, Is.EqualTo(caller.Token));
        Assert.That(resolves, Is.EqualTo(2));
        var retry = OHsExportPlanner.Plan(input, default, resolve: _ => audio);
        Assert.That(retry.Banks.Values.Single().Values.Single().Sources.Single().GainPercent, Is.EqualTo(200));
        Assert.That(OHsExportAudio.IsPlaybackEquivalent(input, retry, _ => audio, default), Is.True);
    }

    [Test]
    public void PlannerCancellationDuringClippingProofUsesCallerToken()
    {
        using var audio = DecodedAudio.CopyFrom([.2f, .2f]);
        using var caller = new CancellationTokenSource();
        var input = OHsExportRampPlanningTests.Input([], [0]);
        var error = Assert.Catch<OperationCanceledException>(() => OHsExportPlanner.Plan(input, caller.Token, resolve: _ =>
        {
            caller.Cancel(); return audio;
        }));
        Assert.That(error.CancellationToken, Is.EqualTo(caller.Token));
        Assert.That(audio.FrameCount, Is.EqualTo(1));
    }

    private static IReadOnlyList<IReadOnlyList<OHsExportRepresentation>> Recipes() => new[] { "a.wav", "b.wav" }
        .Select(file => OHsExportRepresentations.Build([new(100, file, 100, "Normal", SampleType.Normal, 0)], default)).ToArray();

    [TestCase(false), TestCase(true)]
    public void LayoutUserCancellationAbortsBeforeOrAfterCandidate(bool afterCandidate)
    {
        using var caller = new CancellationTokenSource();
        if (!afterCandidate) caller.Cancel();
        var progress = new InlineProgress<OHsExportProgress>(p =>
        {
            if (!p.Phase.Contains("files")) return;
            caller.Cancel();
        });
        var error = Assert.Catch<OperationCanceledException>(() => OHsExportLayoutSearch.Find(Recipes(), 2, caller.Token, progress));
        Assert.That(error.CancellationToken, Is.EqualTo(caller.Token));
        var retry = OHsExportLayoutSearch.Find(Recipes(), 2, default);
        Assert.That(retry.Sum(b => b.Count), Is.EqualTo(2));
        Assert.That(retry.SelectMany(b => b.Values).SelectMany(a => a.Sources).Select(s => s.FileName),
            Is.EquivalentTo(new[] { "a.wav", "b.wav" }));
    }

    [TestCase(false), TestCase(true)]
    public void LayoutPropagatesProgressErrors(bool unrelatedCancellation)
    {
        using var caller = new CancellationTokenSource();
        using var other = new CancellationTokenSource();
        Exception expected = unrelatedCancellation ? new OperationCanceledException(other.Token) : new IOException("progress failed");
        var progress = new InlineProgress<OHsExportProgress>(_ => { caller.Cancel(); throw expected; });
        Assert.That(Assert.Catch(() => OHsExportLayoutSearch.Find(Recipes(), 2, caller.Token, progress)), Is.SameAs(expected));
    }

    [Test]
    public void LayoutProvenCapacityFailureIsPreserved()
        => Assert.Throws<InvalidOperationException>(() => OHsExportLayoutSearch.Find(Recipes(), 1, default));

    private static OHsExportRepresentation Choice(string file, SampleType type) => new("Normal", "Normal", 1,
        new Dictionary<OHsExportSlot, OHsExportAsset> { [new("Normal", type)] = OHsExportAsset.Create([(file, 100)]) }, 0, 0);

    private static (int Time, IReadOnlyList<OHsExportRepresentation> Choices)[] Groups()
    {
        var a = Choice("a.wav", SampleType.Normal);
        var b = Choice("b.wav", SampleType.Whistle);
        var c = Choice("c.wav", SampleType.Normal);
        return [(100, new[] { a }), (200, new[] { b }), (300, new[] { c }), (400, new[] { b })];
    }
    private static OHsExportTimingPoint[] Required() => [new(0, "500", 4, 1, 1, 100, 0, OHsExportTimingKind.SourceRed)];

    [TestCase(false, false), TestCase(false, true), TestCase(true, false), TestCase(true, true)]
    public void TimelineInterruptedValidationAborts(bool hasIncumbent, bool observesCancellation)
    {
        using var caller = new CancellationTokenSource();
        var calls = 0;
        bool Accept(OHsExportPlan plan, CancellationToken searchToken)
        {
            calls++;
            if (hasIncumbent && calls == 1) return true;
            caller.Cancel();
            if (observesCancellation) searchToken.ThrowIfCancellationRequested();
            return true;
        }
        var error = Assert.Catch<OperationCanceledException>(() => OHsExportLayoutSearch.FindTimeline(Groups(), Required(), 1, caller.Token, null, Accept));
        Assert.That(error.CancellationToken, Is.EqualTo(caller.Token));
        Assert.That(calls, Is.EqualTo(hasIncumbent ? 2 : 1));
        var retry = OHsExportLayoutSearch.FindTimeline(Groups(), Required(), 1, default, null);
        Assert.That((retry.AudioFileCount, retry.AdditionalPointCount), Is.EqualTo((3, 1)));
    }

    [Test]
    public void TimelineRejectedSeedCannotBecomeCancellationFallback()
    {
        using var caller = new CancellationTokenSource();
        var error = Assert.Catch<OperationCanceledException>(() => OHsExportLayoutSearch.FindTimeline(Groups(), Required(), 1, caller.Token, null,
            (_, _) => { caller.Cancel(); return false; }));
        Assert.That(error.CancellationToken, Is.EqualTo(caller.Token));
    }

    [TestCase(false), TestCase(true)]
    public void TimelineUserCancellationAbortsBeforeOrAfterCandidate(bool afterCandidate)
    {
        using var caller = new CancellationTokenSource();
        if (!afterCandidate) caller.Cancel();
        var progress = new InlineProgress<OHsExportProgress>(p =>
        {
            if (!p.Phase.Contains("files")) return;
            caller.Cancel();
        });
        var error = Assert.Catch<OperationCanceledException>(() => OHsExportLayoutSearch.FindTimeline(Groups(), Required(), 1, caller.Token, progress));
        Assert.That(error.CancellationToken, Is.EqualTo(caller.Token));
        var retry = OHsExportLayoutSearch.FindTimeline(Groups(), Required(), 1, default, null);
        Assert.That((retry.AudioFileCount, retry.AdditionalPointCount), Is.EqualTo((3, 1)));
        Assert.That(retry.Circles.Select(c => c.Timestamp), Is.EqualTo(new[] { 100, 200, 300, 400 }));
    }

    [TestCase(false), TestCase(true)]
    public void TimelinePropagatesAcceptanceErrors(bool unrelatedCancellation)
    {
        using var caller = new CancellationTokenSource();
        using var other = new CancellationTokenSource();
        Exception expected = unrelatedCancellation ? new OperationCanceledException(other.Token) : new IOException("validation failed");
        Assert.That(Assert.Catch(() => OHsExportLayoutSearch.FindTimeline(Groups(), Required(), 1, caller.Token, null,
            (_, _) => { caller.Cancel(); throw expected; })), Is.SameAs(expected));
    }

    [Test]
    public void TimelineExhaustiveFailuresArePreserved()
    {
        Assert.Throws<InvalidOperationException>(() => OHsExportLayoutSearch.FindTimeline(Groups(), Required(), 999, default, null));
        Assert.Throws<InvalidDataException>(() => OHsExportLayoutSearch.FindTimeline(Groups(), Required(), 1, default, null, (_, _) => false));
    }

    [TestCase(false), TestCase(true)]
    public void PlannerPreservesRampPlayback(bool nonlinear)
    {
        using var audio = DecodedAudio.CopyFrom(Enumerable.Repeat(nonlinear ? .999f : .2f, 4000).ToArray());
        var input = OHsExportRampPlanningTests.Input([new(0, nonlinear ? 80 : 10, IsRamp: true), new(1000, nonlinear ? 100 : 80)], [100, 120]);
        var plan = OHsExportPlanner.Plan(input, default, resolve: _ => audio);
        Assert.That(plan.Circles.Select(c => c.Timestamp), Is.EqualTo(new[] { 100, 120 }));
        Assert.That(plan.Banks.Keys.All(i => i is >= 1 and <= 999), Is.True);
        Assert.That(OHsExportAudio.IsPlaybackEquivalent(input, plan, _ => audio, default), Is.True);
        if (nonlinear)
        {
            var pcm = OHsExportPlaybackOracle.Render(OHsExportBeatmapWriter.Write(input, plan), _ => audio.Retain(), 8000);
            Assert.That(pcm[6000 * 2], Is.EqualTo(1), "The loud overlapping hits still exercise global clipping.");
        }
        Assert.That(OHsExportPlanner.Plan(input, default, resolve: _ => audio).Circles.Count, Is.EqualTo(2));
    }

    [Test]
    public void PlannerHonorsLastAvailableIndex()
    {
        using var audio = DecodedAudio.CopyFrom([.2f, .2f]);
        var baseline = OHsExportRampPlanningTests.Input([new(0, 40)], [100, 200]);
        var input = new OHsExportInput(baseline.Options with { StartingIndex = 999 }, baseline.Beatmap, baseline.Voices, baseline.RequiredPoints, baseline.ControlPoints);
        var plan = OHsExportPlanner.Plan(input, default, resolve: _ => audio);
        Assert.That(plan.Banks.Keys, Is.EqualTo(new[] { 999 }));
        Assert.That(OHsExportAudio.IsPlaybackEquivalent(input, plan, _ => audio, default), Is.True);
    }

    [Test]
    public void PlaybackValidationHonorsCancellationOnEmptyInput()
    {
        using var caller = new CancellationTokenSource(); caller.Cancel();
        var input = OHsExportRampPlanningTests.Input([], []);
        var plan = new OHsExportPlan([], [], new Dictionary<int, IReadOnlyDictionary<OHsExportSlot, OHsExportAsset>>(), 0);
        Assert.Catch<OperationCanceledException>(() => OHsExportAudio.IsPlaybackEquivalent(input, plan, _ => throw new AssertionException("no sources"), caller.Token));
    }
}
