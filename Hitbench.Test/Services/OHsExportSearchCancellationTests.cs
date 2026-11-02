using Hitbench.Core.Enums;
using Hitbench.Resources.DataModels;
using Hitbench.Services;

namespace Hitbench.Test.Services;

public class OHsExportSearchCancellationTests
{
    [Test]
    public void SearchCanAcceptFirstCandidateAfterFormerDeadline()
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
        var progress = new InlineProgress<OHsExportProgress>(_ => DelayOnce());
        var layout = OHsExportLayoutSearch.Find(Recipes(), 2, caller.Token, progress);
        Assert.That(layout.Count, Is.EqualTo(2));
        Assert.That(layout.SelectMany(b => b.Values).SelectMany(a => a.Sources).Select(v => v.FileName), Is.EquivalentTo(new[] { "a.wav", "b.wav" }));
        Assert.That(delayed, Is.True);
        Assert.That(watch.Elapsed, Is.GreaterThanOrEqualTo(TimeSpan.FromSeconds(5)));
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

    [TestCase(false), TestCase(true)]
    public void PlannerPreservesRampPlayback(bool nonlinear)
    {
        using var audio = DecodedAudio.CopyFrom(Enumerable.Repeat(nonlinear ? .999f : .2f, 4000).ToArray());
        var input = OHsExportRampPlanningTests.Input([new(0, nonlinear ? 80 : 10, IsRamp: true), new(1000, nonlinear ? 100 : 80)], [100, 120]);
        var plan = OHsExportPlanner.Plan(input, default);
        Assert.That(plan.Circles.Select(c => c.Timestamp), Is.EqualTo(new[] { 100, 120 }));
        Assert.That(plan.Banks.Keys.All(i => i is >= 1 and <= 999), Is.True);

        if (nonlinear)
        {
            var pcm = OHsExportPlaybackOracle.Render(OHsExportBeatmapWriter.Write(input, plan), _ => audio.Retain(), 8000);
            Assert.That(pcm[6000 * 2], Is.EqualTo(1), "The loud overlapping hits still exercise global clipping.");
        }
        Assert.That(OHsExportPlanner.Plan(input, default).Circles.Count, Is.EqualTo(2));
    }

    [Test]
    public void PlannerHonorsLastAvailableIndex()
    {
        using var audio = DecodedAudio.CopyFrom([.2f, .2f]);
        var baseline = OHsExportRampPlanningTests.Input([new(0, 40)], [100, 200]);
        var input = new OHsExportInput(baseline.Options with { StartingIndex = 999 }, baseline.Beatmap, baseline.Voices, baseline.RequiredPoints, baseline.ControlPoints);
        var plan = OHsExportPlanner.Plan(input, default);
        Assert.That(plan.Banks.Keys, Is.EqualTo(new[] { 999 }));
    }

}
