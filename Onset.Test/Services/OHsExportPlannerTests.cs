using Onset.Core.Enums;
using Onset.Resources.DataModels;
using Onset.Services;
namespace Onset.Test.Services;

public class OHsExportPlannerTests
{
    private static OHsExportVoice Voice(int time, string file, string set = "Normal", SampleType type = SampleType.Clap) => new(time, file, 100, set, type, 0);
    private static OHsExportInput Input(OHsExportVoice[] voices, int start = 1, params int[] points)
    {
        var map = OHsExportBeatmap.Parse("[TimingPoints]\n0,500,4,1,1,100,1,0\n");
        return new(new("Hitsounds", start, ".ogg", ".ogg", false), map, voices,
            map.TimingPoints.Concat(points.Select(t => new OHsExportTimingPoint(t, "-100", 4, 1, start, 40, 0, OHsExportTimingKind.ProjectControl))).OrderBy(p => p.Timestamp).ToArray());
    }
    [TestCase(100, 0), TestCase(100, 1), TestCase(100, 2), TestCase(0, 1), TestCase(int.MaxValue, 0), TestCase(int.MaxValue - 1, 1)]
    public void NeighboringVoicesUseOneMillisecondWindow(int anchor, int gap)
    {
        var plan = OHsExportPlanner.Plan(Input([Voice(anchor + gap, "b.wav"), Voice(anchor, "a.wav")]), default);
        Assert.That(plan.Circles.Select(c => c.Timestamp), Is.EqualTo(gap <= 1 ? new[] { anchor } : new[] { anchor, anchor + gap }));
    }

    [TestCase(0), TestCase(100), TestCase(int.MaxValue - 2)]
    public void GroupingDoesNotChain(int anchor)
    {
        var plan = OHsExportPlanner.Plan(Input([Voice(anchor + 2, "c.wav"), Voice(anchor, "a.wav"), Voice(anchor + 1, "b.wav")]), default);
        Assert.That(plan.Circles.Select(c => c.Timestamp), Is.EqualTo(new[] { anchor, anchor + 2 }));
        var slot = new OHsExportSlot("Normal", SampleType.Clap);
        Assert.That(plan.Banks[plan.Circles[0].SampleIndex][slot].Sources.Select(s => s.FileName), Is.EquivalentTo(new[] { "a.wav", "b.wav" }));
        Assert.That(plan.Banks[plan.Circles[1].SampleIndex][slot].Sources.Select(s => s.FileName), Is.EqualTo(new[] { "c.wav" }));
    }

    [Test]
    public void MergedNeighborsRetainRepeatedGainAndIndependentNormalSlot()
    {
        var input = Input([Voice(100, "normal.wav", "Normal", SampleType.Normal), Voice(100, "repeat.wav", "Soft") with { GainPercent = 70 }, Voice(101, "repeat.wav", "Soft") with { GainPercent = 40 }]);
        var plan = OHsExportPlanner.Plan(input, default);
        Assert.That(plan.Circles.Select(c => c.Timestamp), Is.EqualTo(new[] { 100 }));
        var circle = plan.Circles.Single();
        var bank = plan.Banks[circle.SampleIndex];
        Assert.That(bank[new("Normal", SampleType.Normal)].Sources, Is.EqualTo(new[] { ("normal.wav", 100) }));
        Assert.That(bank[new("Soft", SampleType.Clap)].Sources, Is.EqualTo(new[] { ("repeat.wav", 110) }));
        Assert.That(circle.HitSound & 8, Is.EqualTo(8));
        Assert.That(input.Voices.Select(v => v.Timestamp), Is.EqualTo(new[] { 100, 100, 101 }));
        var repeated = OHsExportPlanner.Plan(input, default);
        Assert.That(repeated.Circles, Is.EqualTo(plan.Circles));
        Assert.That(repeated.Banks.SelectMany(b => b.Value.Select(s => (b.Key, s.Key, s.Value.Id))), Is.EqualTo(plan.Banks.SelectMany(b => b.Value.Select(s => (b.Key, s.Key, s.Value.Id)))));
    }

    [Test]
    public void EarlierRequiredPointCanCarryFutureBankConflict()
    {
        var plan = OHsExportPlanner.Plan(Input([Voice(100, "a.wav"), Voice(200, "b.wav", "Soft", SampleType.Whistle), Voice(300, "c.wav")], 1, 150), default);
        Assert.That(plan.AdditionalPointCount, Is.Zero);
        Assert.That(plan.Circles[1].SampleIndex, Is.EqualTo(plan.Circles[2].SampleIndex));
        Assert.That(plan.Circles[0].SampleIndex, Is.Not.EqualTo(plan.Circles[2].SampleIndex));
    }
    [Test]
    public void DenseCirclesSwitchAfterPreviousLookupAndReuseBank()
    {
        var plan = OHsExportPlanner.Plan(Input([Voice(1000, "a.wav"), Voice(1002, "b.wav"), Voice(1100, "a.wav")]), default);
        var point = plan.TimingPoints.First(p => p.Kind == OHsExportTimingKind.BankSwitch);
        Assert.That(point.Timestamp, Is.GreaterThan(1005).And.LessThanOrEqualTo(1007));
        Assert.That(plan.AdditionalPointCount, Is.EqualTo(2));
        Assert.That(plan.Circles[0].SampleIndex, Is.EqualTo(plan.Circles[2].SampleIndex));
        var free = OHsExportPlanner.Plan(Input([Voice(1000, "a.wav"), Voice(1002, "b.wav")], 1, 1006), default);
        Assert.That(free.AdditionalPointCount, Is.Zero);
    }
    [Test]
    public void LastAvailableBankTriesAlternativeSilentNormalSet()
    {
        var plan = OHsExportPlanner.Plan(Input([Voice(100, "normal.wav", "Normal", SampleType.Normal), Voice(200, "clap.wav", "Soft")], 999), default);
        Assert.That(plan.Banks.Keys, Is.EqualTo(new[] { 999 }));
        Assert.That(plan.Circles[1].NormalSet, Is.Not.EqualTo("Normal"));
        Assert.Throws<InvalidOperationException>(() => OHsExportPlanner.Plan(Input([Voice(100, "a.wav"), Voice(200, "b.wav")], 999), default));
    }
    [Test]
    public void ZeroGainSourcesReuseOneSilentSlotAtTheLastAvailableIndex()
    {
        var plan = OHsExportPlanner.Plan(Input([Voice(100, "a.wav") with { GainPercent = 0 }, Voice(200, "b.wav") with { GainPercent = 0 }], 999), default);
        Assert.That(plan.Banks.Keys, Is.EqualTo(new[] { 999 }));
        Assert.That(plan.Circles.Select(c => c.Timestamp), Is.EqualTo(new[] { 100, 200 }));
        Assert.That(plan.AdditionalPointCount, Is.Zero);
    }
    [Test]
    public void FutureFirstRowOnlyNeedsAnchorWhenNativeFallbackWouldChangeEarlierSamples()
    {
        var input = Input([Voice(100, "a.wav")]);
        var future = input.RequiredPoints.Select(p => p with { Timestamp = 500 }).ToArray();
        Assert.That(OHsExportPlanner.Plan(new(input.Options, input.Beatmap, input.Voices, future), default).AdditionalPointCount, Is.Zero);
        Assert.That(OHsExportPlanner.Plan(new(input.Options, input.Beatmap, input.Voices, future.Select(p => p with { Volume = 40 }).ToArray()), default).AdditionalPointCount, Is.EqualTo(1));
    }
    [Test]
    public void EmptyArrangementRetainsRequiredRowsAndCancellationIsObserved()
    {
        Assert.That(OHsExportPlanner.Plan(Input([], 1, 50, 70), default).TimingPoints, Has.Count.EqualTo(3));
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() => OHsExportPlanner.Plan(Input([]), cancellation.Token));
    }

    [Test]
    public void BoundedPlansMatchIndependentCompleteAssignmentOracle()
    {
        var random = new Random(24061);
        for (var iteration = 0; iteration < 80; iteration++)
        {
            var voices = Enumerable.Range(0, 6).Select(i => Voice(100 + i * 100, $"{random.Next(3)}.wav", "Normal", SampleType.Normal)).ToArray();
            var opportunities = Enumerable.Range(0, 3).Where(_ => random.Next(2) == 0).Select(i => 150 + 100 * i).ToArray();
            for (var capacity = 1; capacity <= 3; capacity++)
            {
                var input = Input(voices, 1000 - capacity, opportunities);
                var groups = voices.Select(v => (v.Timestamp, (IReadOnlyList<IReadOnlyDictionary<OHsExportSlot, string>>)new IReadOnlyDictionary<OHsExportSlot, string>[] { new Dictionary<OHsExportSlot, string> { [new("Normal", SampleType.Normal)] = v.FileName } })).ToArray();
                var minimum = OHsExportBruteForceOracle.MinimumCost(groups, opportunities.Select(t => (decimal)t).ToArray(), capacity);
                if (minimum is null) Assert.Throws<InvalidOperationException>(() => OHsExportPlanner.Plan(input, default));
                else
                {
                    var plan = OHsExportPlanner.Plan(input, default);
                    Assert.That((plan.AudioFileCount, plan.AdditionalPointCount), Is.EqualTo(minimum.Value), $"iteration {iteration}, capacity {capacity}");
                }
            }
        }
    }
    [Test]
    public void AlternateTwoSlotRepresentationsMatchIndependentOracle()
    {
        var random = new Random(12315);
        for (var iteration = 0; iteration < 20; iteration++)
        {
            var voices = Enumerable.Range(0, 4).Select(i => Voice(100 + i * 100, $"{random.Next(2)}.wav", "Normal", random.Next(2) == 0 ? SampleType.Normal : SampleType.Clap)).ToArray();
            var opportunities = Enumerable.Range(0, 3).Where(_ => random.Next(2) == 0).Select(i => 150 + 100 * i).ToArray();
            var silence = OHsExportAsset.Create([]).Id;
            var groups = voices.Select(v => (v.Timestamp, (IReadOnlyList<IReadOnlyDictionary<OHsExportSlot, string>>)(v.Type == SampleType.Normal
                ? new IReadOnlyDictionary<OHsExportSlot, string>[] { new Dictionary<OHsExportSlot, string> { [new("Normal", SampleType.Normal)] = OHsExportAsset.Create([(v.FileName, 100)]).Id } }
                : new[] { "Normal", "Soft", "Drum" }.Select(set => (IReadOnlyDictionary<OHsExportSlot, string>)new Dictionary<OHsExportSlot, string> { [new(set, SampleType.Normal)] = silence, [new("Normal", SampleType.Clap)] = OHsExportAsset.Create([(v.FileName, 100)]).Id }).ToArray()))).ToArray();
            for (var capacity = 1; capacity <= 3; capacity++)
            {
                var minimum = OHsExportBruteForceOracle.MinimumCost(groups, opportunities.Select(t => (decimal)t).ToArray(), capacity);
                if (minimum is null) Assert.Throws<InvalidOperationException>(() => OHsExportPlanner.Plan(Input(voices, 1000 - capacity, opportunities), default));
                else
                {
                    var plan = OHsExportPlanner.Plan(Input(voices, 1000 - capacity, opportunities), default);
                    Assert.That((plan.AudioFileCount, plan.AdditionalPointCount), Is.EqualTo(minimum.Value), $"iteration {iteration}, capacity {capacity}");
                }
            }
        }
    }
    [Test]
    public void MinimumFilePlansChooseDeterministicRecipes()
    {
        for (var i = 0; i < 12; i++)
        {
            var voices = new[]
            {
                Voice(100, "d.wav", "Normal", SampleType.Normal), Voice(100, $"a{i}.wav", "Soft", SampleType.Whistle), Voice(100, "b.wav", "Soft", SampleType.Finish), Voice(100, "c.wav", "Drum", SampleType.Clap),
                Voice(200, "e.wav", "Normal", SampleType.Normal), Voice(200, $"a{i}.wav", "Soft", SampleType.Whistle), Voice(200, "c.wav", "Drum", SampleType.Clap)
            };
            var plan = OHsExportPlanner.Plan(Input(voices, 1, 150), default);
            Assert.That(plan.AdditionalPointCount, Is.Zero);
            Assert.That(plan.AudioFileCount, Is.EqualTo(5));
            var repeated = OHsExportPlanner.Plan(Input(voices, 1, 150), default);
            Assert.That(repeated.Banks.SelectMany(b => b.Value.Select(s => (b.Key, s.Key, s.Value.Id))),
                Is.EqualTo(plan.Banks.SelectMany(b => b.Value.Select(s => (b.Key, s.Key, s.Value.Id)))));
        }
    }

    [TestCase(250, 1)]
    [TestCase(150, 0)]
    public void MinimumFilesTakePriorityOverTimingRows(int opportunity, int added)
    {
        var input = Input([
            Voice(100, "a.wav", "Normal", SampleType.Normal),
            Voice(200, "c.wav", "Soft", SampleType.Clap),
            Voice(300, "b.wav", "Normal", SampleType.Normal),
            Voice(400, "c.wav", "Soft", SampleType.Clap)
        ], 1, opportunity);
        var plan = OHsExportPlanner.Plan(input, default);
        Assert.That(plan.AudioFileCount, Is.EqualTo(4));
        Assert.That(plan.AdditionalPointCount, Is.EqualTo(added));
    }

    [Test]
    public void IndependentSamplesRemainUnmixedWhenMixingWouldSaveAFile()
    {
        var voices = new[] { 100, 200 }.SelectMany(time => new[]
        {
            Voice(time, "a.wav", "Normal", SampleType.Normal), Voice(time, "c.wav", "Soft", SampleType.Clap)
        }).ToArray();
        var plan = OHsExportPlanner.Plan(Input(voices), default);
        Assert.That(plan.AudioFileCount, Is.EqualTo(2));
        Assert.That(plan.Banks.Values.SelectMany(b => b.Values).Select(a => a.Sources.Count), Is.All.EqualTo(1));
    }

    [Test]
    public void RepeatedSingleAndMixtureCountPhysicalSilenceFiles()
    {
        var voices = new[] { 100, 200, 300, 400 }.Select(time => Voice(time, "a.wav", "Soft"))
            .Concat(new[] { 200, 400 }.Select(time => Voice(time, "b.wav", "Soft"))).ToArray();
        var plan = OHsExportPlanner.Plan(Input(voices), default);
        Assert.That(plan.AudioFileCount, Is.EqualTo(4));
        Assert.That(plan.Banks.Values.SelectMany(b => b.Values).Select(a => a.Id).Distinct().Count(), Is.EqualTo(3));
        Assert.That(plan.AdditionalPointCount, Is.EqualTo(3));
        Assert.That(plan.Circles[0].SampleIndex, Is.EqualTo(plan.Circles[2].SampleIndex));
        Assert.That(plan.Circles[1].SampleIndex, Is.EqualTo(plan.Circles[3].SampleIndex));
    }

    [TestCase(1)] [TestCase(2)] [TestCase(3)]
    public void MixedRepresentativeChoicesMatchIndependentOracle(int capacity)
    {
        var voices = new[] { 100, 300 }.SelectMany(time => new[]
        {
            Voice(time, "a.wav", "Soft", SampleType.Whistle), Voice(time, "b.wav", "Drum", SampleType.Clap)
        }).Append(Voice(200, "a.wav", "Soft", SampleType.Whistle)).ToArray();
        var silence = OHsExportAsset.Create([]).Id;
        var mixed = OHsExportAsset.Create([("a.wav", 100), ("b.wav", 100)]).Id;
        var direct = OHsExportAsset.Create([("a.wav", 100)]).Id;
        var choices = new[] { "Normal", "Soft", "Drum" }.SelectMany(set => new[]
        {
            (IReadOnlyDictionary<OHsExportSlot, string>)new Dictionary<OHsExportSlot, string>
                { [new(set, SampleType.Normal)] = silence, [new("Soft", SampleType.Whistle)] = mixed },
            new Dictionary<OHsExportSlot, string>
                { [new(set, SampleType.Normal)] = silence, [new("Drum", SampleType.Clap)] = mixed }
        }).ToArray();
        var singleton = new[] { "Normal", "Soft", "Drum" }.Select(set =>
            (IReadOnlyDictionary<OHsExportSlot, string>)new Dictionary<OHsExportSlot, string>
                { [new(set, SampleType.Normal)] = silence, [new("Soft", SampleType.Whistle)] = direct }).ToArray();
        var groups = new (int Timestamp, IReadOnlyList<IReadOnlyDictionary<OHsExportSlot, string>> Choices)[]
            { (100, choices), (200, singleton), (300, choices) };
        var expected = OHsExportBruteForceOracle.MinimumCost(groups, [], capacity);
        Assert.That(expected, Is.EqualTo((3, 0)));
        var plan = OHsExportPlanner.Plan(Input(voices, 1000 - capacity), default);
        Assert.That((plan.AudioFileCount, plan.AdditionalPointCount), Is.EqualTo(expected!.Value));
    }
}
