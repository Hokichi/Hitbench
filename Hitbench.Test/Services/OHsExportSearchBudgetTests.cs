using Hitbench.Core.Enums;
using Hitbench.Resources.DataModels;
using Hitbench.Services;

namespace Hitbench.Test.Services;

public class OHsExportSearchBudgetTests
{
    [Test]
    public void ClippingRecipeStillRequiresExactAcceptance()
    {
        using var audio = DecodedAudio.CopyFrom(Enumerable.Repeat(.8f, 4000).ToArray());
        var baseline = OHsExportRampPlanningTests.Input([], [0, 20]);
        var input = new OHsExportInput(baseline.Options, baseline.Beatmap,
            baseline.Voices.Concat(baseline.Voices).ToArray(), baseline.RequiredPoints);
        using var budget = new CancellationTokenSource();
        var resolves = 0;
        Assert.Throws<TimeoutException>(() => OHsExportPlanner.Plan(input, default, null, _ =>
        {
            if (++resolves == 2) budget.Cancel();
            return audio;
        }, budget.Token));
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
    public void LayoutExpirationReturnsOnlyCompleteCandidate(bool afterCandidate)
    {
        using var budget = new CancellationTokenSource();
        var progress = new InlineProgress<OHsExportProgress>(p =>
        {
            if (p.Phase.Contains("files") == afterCandidate) budget.Cancel();
        });
        if (!afterCandidate)
            Assert.Throws<TimeoutException>(() => OHsExportLayoutSearch.Find(Recipes(), 2, default, progress, budget.Token));
        else
        {
            var layout = OHsExportLayoutSearch.Find(Recipes(), 2, default, progress, budget.Token);
            Assert.That(layout.Count, Is.EqualTo(2));
            Assert.That(layout.SelectMany(b => b.Values).SelectMany(a => a.Sources).Select(s => s.FileName),
                Is.EquivalentTo(new[] { "a.wav", "b.wav" }));
            OHsExportLayoutSearch.Find(Recipes(), 2, default);
            Assert.That(layout.Sum(b => b.Count), Is.EqualTo(2));
        }
        Assert.That(OHsExportLayoutSearch.Find(Recipes(), 2, default).Sum(b => b.Count), Is.EqualTo(2));
    }

    [TestCase(false, false), TestCase(false, true), TestCase(true, false), TestCase(true, true)]
    public void LayoutUserCancellationWins(bool afterCandidate, bool expireBudget)
    {
        using var caller = new CancellationTokenSource();
        using var budget = new CancellationTokenSource();
        if (!afterCandidate) caller.Cancel();
        var progress = new InlineProgress<OHsExportProgress>(p =>
        {
            if (!p.Phase.Contains("files")) return;
            caller.Cancel();
            if (expireBudget) budget.Cancel();
        });
        var error = Assert.Catch<OperationCanceledException>(() => OHsExportLayoutSearch.Find(Recipes(), 2, caller.Token, progress, budget.Token));
        Assert.That(error.CancellationToken, Is.EqualTo(caller.Token));
    }

    [TestCase(false), TestCase(true)]
    public void LayoutExpirationDoesNotHideProgressErrors(bool unrelatedCancellation)
    {
        using var budget = new CancellationTokenSource();
        using var other = new CancellationTokenSource();
        Exception expected = unrelatedCancellation ? new OperationCanceledException(other.Token) : new IOException("progress failed");
        var progress = new InlineProgress<OHsExportProgress>(_ => { budget.Cancel(); throw expected; });
        Assert.That(Assert.Catch(() => OHsExportLayoutSearch.Find(Recipes(), 2, default, progress, budget.Token)), Is.SameAs(expected));
    }

    [Test]
    public void LayoutProvenCapacityFailureIsNotTimeout()
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

    [TestCase(false), TestCase(true)]
    public void TimelineExpirationReturnsOnlyAcceptedCandidate(bool afterCandidate)
    {
        using var budget = new CancellationTokenSource();
        if (!afterCandidate) budget.Cancel();
        var progress = new InlineProgress<OHsExportProgress>(p => { if (p.Phase.Contains("files")) budget.Cancel(); });
        if (!afterCandidate)
            Assert.Throws<TimeoutException>(() => OHsExportLayoutSearch.FindTimeline(Groups(), Required(), 1, default, progress, budgetCancellation: budget.Token));
        else
        {
            var plan = OHsExportLayoutSearch.FindTimeline(Groups(), Required(), 1, default, progress, budgetCancellation: budget.Token);
            Assert.That((plan.AudioFileCount, plan.AdditionalPointCount), Is.EqualTo((3, 2)));
            Assert.That(plan.Circles.Select(c => c.Timestamp), Is.EqualTo(new[] { 100, 200, 300, 400 }));
            var retry = OHsExportLayoutSearch.FindTimeline(Groups(), Required(), 1, default, null);
            Assert.That(retry.AdditionalPointCount, Is.EqualTo(1));
            Assert.That(plan.AdditionalPointCount, Is.EqualTo(2));
            Assert.That(plan.Banks.Values.Sum(b => b.Count), Is.EqualTo(3));
        }
    }

    [TestCase(false, false), TestCase(false, true), TestCase(true, false), TestCase(true, true)]
    public void TimelineInterruptedValidationCannotReplaceIncumbent(bool hasIncumbent, bool observesCancellation)
    {
        using var budget = new CancellationTokenSource();
        var calls = 0;
        bool Accept(OHsExportPlan plan, CancellationToken searchToken)
        {
            calls++;
            if (hasIncumbent && calls == 1) return true;
            budget.Cancel();
            if (observesCancellation) searchToken.ThrowIfCancellationRequested();
            return true;
        }
        if (!hasIncumbent)
            Assert.Throws<TimeoutException>(() => OHsExportLayoutSearch.FindTimeline(Groups(), Required(), 1, default, null, Accept, budgetCancellation: budget.Token));
        else
        {
            var plan = OHsExportLayoutSearch.FindTimeline(Groups(), Required(), 1, default, null, Accept, budgetCancellation: budget.Token);
            Assert.That((plan.AudioFileCount, plan.AdditionalPointCount), Is.EqualTo((3, 2)));
            Assert.That(calls, Is.EqualTo(2));
        }
    }

    [Test]
    public void TimelineRejectedSeedIsNeverReturnedOnExpiration()
    {
        using var budget = new CancellationTokenSource();
        Assert.Throws<TimeoutException>(() => OHsExportLayoutSearch.FindTimeline(Groups(), Required(), 1, default, null,
            (_, _) => { budget.Cancel(); return false; }, budgetCancellation: budget.Token));
    }

    [TestCase(false, false), TestCase(false, true), TestCase(true, false), TestCase(true, true)]
    public void TimelineUserCancellationWins(bool afterCandidate, bool expireBudget)
    {
        using var caller = new CancellationTokenSource();
        using var budget = new CancellationTokenSource();
        if (!afterCandidate) { caller.Cancel(); if (expireBudget) budget.Cancel(); }
        var progress = new InlineProgress<OHsExportProgress>(p =>
        {
            if (!p.Phase.Contains("files")) return;
            caller.Cancel(); if (expireBudget) budget.Cancel();
        });
        var error = Assert.Catch<OperationCanceledException>(() => OHsExportLayoutSearch.FindTimeline(Groups(), Required(), 1, caller.Token, progress, budgetCancellation: budget.Token));
        Assert.That(error.CancellationToken, Is.EqualTo(caller.Token));
    }

    [TestCase(false), TestCase(true)]
    public void TimelineExpirationDoesNotHideAcceptanceErrors(bool unrelatedCancellation)
    {
        using var budget = new CancellationTokenSource();
        using var other = new CancellationTokenSource();
        Exception expected = unrelatedCancellation ? new OperationCanceledException(other.Token) : new IOException("validation failed");
        Assert.That(Assert.Catch(() => OHsExportLayoutSearch.FindTimeline(Groups(), Required(), 1, default, null,
            (_, _) => { budget.Cancel(); throw expected; }, budgetCancellation: budget.Token)), Is.SameAs(expected));
    }

    [Test]
    public void TimelineExhaustiveFailuresAreNotTimeouts()
    {
        Assert.Throws<InvalidOperationException>(() => OHsExportLayoutSearch.FindTimeline(Groups(), Required(), 999, default, null));
        Assert.Throws<InvalidDataException>(() => OHsExportLayoutSearch.FindTimeline(Groups(), Required(), 1, default, null, (_, _) => false));
    }

    [Test]
    public void TimelineUserCancellationDuringValidationIsNotFallback()
    {
        using var caller = new CancellationTokenSource();
        var error = Assert.Catch<OperationCanceledException>(() => OHsExportLayoutSearch.FindTimeline(Groups(), Required(), 1, caller.Token, null,
            (_, searchToken) => { caller.Cancel(); searchToken.ThrowIfCancellationRequested(); return true; }));
        Assert.That(error.CancellationToken, Is.EqualTo(caller.Token));
    }

    [TestCase(false), TestCase(true)]
    public void PlannerDeadlineFallbackPreservesRampPlayback(bool nonlinear)
    {
        using var audio = DecodedAudio.CopyFrom(Enumerable.Repeat(nonlinear ? .999f : .2f, 4000).ToArray());
        var input = OHsExportRampPlanningTests.Input([new(0, nonlinear ? 80 : 10, IsRamp: true), new(1000, nonlinear ? 100 : 80)], [100, 120]);
        using var budget = new CancellationTokenSource();
        var progress = new InlineProgress<OHsExportProgress>(p => { if (p.Phase.Contains("files")) budget.Cancel(); });
        var plan = OHsExportPlanner.Plan(input, default, progress, _ => audio, budget.Token);
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
    public void PlannerFallbackHonorsLastAvailableIndex()
    {
        using var audio = DecodedAudio.CopyFrom([.2f, .2f]);
        var baseline = OHsExportRampPlanningTests.Input([new(0, 40)], [100, 200]);
        var input = new OHsExportInput(baseline.Options with { StartingIndex = 999 }, baseline.Beatmap, baseline.Voices, baseline.RequiredPoints, baseline.ControlPoints);
        using var budget = new CancellationTokenSource();
        var progress = new InlineProgress<OHsExportProgress>(p => { if (p.Phase.Contains("files")) budget.Cancel(); });
        var plan = OHsExportPlanner.Plan(input, default, progress, _ => audio, budget.Token);
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
