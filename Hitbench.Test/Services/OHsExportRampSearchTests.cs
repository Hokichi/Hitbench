using Hitbench.Core.Enums;
using Hitbench.Resources.DataModels;
using Hitbench.Services;

namespace Hitbench.Test.Services;

public class OHsExportRampSearchTests
{
    [Test]
    public void EqualFileLayoutsChooseFewestTimingRows()
    {
        var a = Choice("a.wav", SampleType.Normal);
        var b = Choice("b.wav", SampleType.Whistle);
        var c = Choice("c.wav", SampleType.Normal);
        (int Time, IReadOnlyList<OHsExportRepresentation> Choices)[] groups = [(100, new[] { a }), (200, new[] { b }), (300, new[] { c }), (400, new[] { b })];
        var plan = OHsExportLayoutSearch.FindTimeline(groups, Required(), 1, default, null);
        Assert.That((plan.AudioFileCount, plan.AdditionalPointCount), Is.EqualTo((3, 1)));
    }

    [TestCase(998), TestCase(999)]
    public void TinyAssignmentsAgreeWithIndependentExhaustiveCost(int start)
    {
        var random = new Random(19);
        for (var fixture = 0; fixture < 100; fixture++)
        {
            var groups = Enumerable.Range(0, 4).Select(i => (Time: 100 + i * 100,
                Choices: (IReadOnlyList<OHsExportRepresentation>)new[] { Choice($"{random.Next(3)}.wav", SampleType.Normal), Choice($"{random.Next(3)}.wav", SampleType.Clap) })).ToArray();
            var oracle = MinimumCost(groups, 1000 - start);
            if (oracle is null) continue;
            var plan = OHsExportLayoutSearch.FindTimeline(groups, Required(), start, default, null);
            Assert.That((plan.AudioFileCount, plan.AdditionalPointCount), Is.EqualTo(oracle.Value));
        }
    }

    [Test]
    public void SameBankGainSwitchUsesDenseRequiredOpportunity()
    {
        (int Time, IReadOnlyList<OHsExportRepresentation> Choices)[] groups = [(1000, new[] { Choice("a.wav", SampleType.Normal) }), (1002, new[] { Choice("a.wav", SampleType.Normal) })];
        var plan = OHsExportLayoutSearch.FindTimeline(groups, [.. Required(), new(1006, "-100", 4, 1, 1, 50, 0, OHsExportTimingKind.ProjectControl)], 1, default, null);
        Assert.That((plan.AudioFileCount, plan.AdditionalPointCount), Is.EqualTo((1, 0)));
        Assert.That(plan.TimingPoints.Single(p => p.Timestamp == 1006).Volume, Is.EqualTo(50));
    }

    private static OHsExportTimingPoint[] Required() => [new(0, "500", 4, 1, 1, 100, 0, OHsExportTimingKind.SourceRed)];
    private static OHsExportRepresentation Choice(string file, SampleType type) => new("Normal", "Normal", 1,
        new Dictionary<OHsExportSlot, OHsExportAsset> { [new("Normal", type)] = OHsExportAsset.Create([(file, 100)]) }, 0, 0);

    private static (int Files, int Rows)? MinimumCost((int Time, IReadOnlyList<OHsExportRepresentation> Choices)[] groups, int capacity)
    {
        (int Files, int Rows)? best = null;
        var banks = new Dictionary<OHsExportSlot, string>[capacity];
        for (var i = 0; i < capacity; i++) banks[i] = new();
        Visit(0, -1, 0);
        return best;
        void Visit(int index, int previousBank, int rows)
        {
            if (index == groups.Length)
            {
                var cost = (Files: banks.Sum(b => b.Count), Rows: rows);
                if (best is null || cost.CompareTo(best.Value) < 0) best = cost;
                return;
            }
            foreach (var choice in groups[index].Choices)
                for (var bank = 0; bank < capacity; bank++)
                {
                    if (choice.Slots.Any(p => banks[bank].TryGetValue(p.Key, out var id) && id != p.Value.Id)) continue;
                    var old = new Dictionary<OHsExportSlot, string>(banks[bank]);
                    foreach (var pair in choice.Slots) banks[bank][pair.Key] = pair.Value.Id;
                    Visit(index + 1, bank, rows + (index > 0 && previousBank != bank ? 1 : 0));
                    banks[bank] = old;
                }
        }
    }
}
