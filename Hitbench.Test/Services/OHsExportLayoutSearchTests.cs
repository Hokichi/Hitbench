using Hitbench.Core.Enums;
using Hitbench.Resources.DataModels;
using Hitbench.Services;

namespace Hitbench.Test.Services;

public class OHsExportLayoutSearchTests
{
    [TestCase(1), TestCase(2)]
    public void AlternativeSlotsMatchIndependentFileMinimum(int capacity)
    {
        var random = new Random(19);
        for (var fixture = 0; fixture < 100; fixture++)
        {
            var recipes = Enumerable.Range(0, 4).Select(i =>
                (IReadOnlyList<OHsExportRepresentation>)new[] { SampleType.Normal, SampleType.Clap }.Select(type =>
                    new OHsExportRepresentation("Normal", "Normal", 1,
                        type == SampleType.Normal
                            ? new Dictionary<OHsExportSlot, OHsExportAsset> { [new("Normal", type)] = OHsExportAsset.Create([($"{random.Next(3)}.wav", 100)]) }
                            : new Dictionary<OHsExportSlot, OHsExportAsset> { [new("Soft", SampleType.Normal)] = OHsExportAsset.Create([]), [new("Normal", type)] = OHsExportAsset.Create([($"{random.Next(3)}.wav", 100)]) }, 0, 0)).ToArray()).ToArray();
            var maps = recipes.Select(r => (IReadOnlyList<IReadOnlyDictionary<OHsExportSlot, string>>)r.Select(c =>
                (IReadOnlyDictionary<OHsExportSlot, string>)c.Slots.ToDictionary(p => p.Key, p => p.Value.Id)).ToArray()).ToArray();
            var minimum = OHsExportBruteForceOracle.MinimumAudioFiles(maps, capacity);
            if (minimum is null) Assert.Throws<InvalidOperationException>(() => OHsExportLayoutSearch.Find(recipes, capacity, default));
            else Assert.That(OHsExportLayoutSearch.Find(recipes, capacity, default).Sum(b => b.Count), Is.EqualTo(minimum.Value));
        }
    }

    private static OHsExportVoice Voice(string file, string set = "Normal", SampleType type = SampleType.Normal, int gain = 100)
        => new(100, file, gain, set, type, 0);

    private static IReadOnlyList<IReadOnlyList<OHsExportRepresentation>> Recipes(params OHsExportVoice[][] groups)
        => groups.Select(g => OHsExportRepresentations.Build(g, default)).ToArray();

    private static string Signature(IEnumerable<IReadOnlyDictionary<OHsExportSlot, OHsExportAsset>> banks)
        => string.Join(';', banks.Select(b => string.Join('|', b.OrderBy(p => p.Key.Sampleset).ThenBy(p => p.Key.Type)
            .Select(p => $"{p.Key}:{p.Value.Id}"))).Order(StringComparer.Ordinal));

    [Test]
    public void BoundedLayoutsMatchIndependentCompleteAssignmentOracle()
    {
        var random = new Random(241026);
        for (var iteration = 0; iteration < 50; iteration++)
        {
            var recipes = Recipes(Enumerable.Range(0, 4).Select(_ => Enumerable.Range(0, random.Next(1, 3))
                .Select(_ => Voice($"s{random.Next(3)}.wav", new[] { "Normal", "Soft", "Drum" }[random.Next(3)],
                    (SampleType)random.Next(4), random.Next(8) == 0 ? 0 : 100)).ToArray()).ToArray());
            var maps = recipes.Select(r => (IReadOnlyList<IReadOnlyDictionary<OHsExportSlot, string>>)r.Select(c =>
                (IReadOnlyDictionary<OHsExportSlot, string>)c.Slots.ToDictionary(p => p.Key, p => p.Value.Id)).ToArray()).ToArray();
            for (var capacity = 1; capacity <= 3; capacity++)
            {
                var expected = OHsExportBruteForceOracle.MinimumAudioFiles(maps, capacity);
                if (expected is null)
                    Assert.Throws<InvalidOperationException>(() => OHsExportLayoutSearch.Find(recipes, capacity, default));
                else
                {
                    var layout = OHsExportLayoutSearch.Find(recipes, capacity, default);
                    Assert.That(layout.Sum(b => b.Count), Is.EqualTo(expected.Value), $"iteration {iteration}, capacity {capacity}");
                    Assert.That(layout.Count, Is.LessThanOrEqualTo(capacity));
                    foreach (var recipe in recipes)
                        Assert.That(recipe.Any(c => layout.Any(b => c.Slots.All(p => b.TryGetValue(p.Key, out var asset) && asset.Id == p.Value.Id))), Is.True);
                }
            }
        }
    }

    [Test]
    public void SharedAdditionNeedsPhysicalCopiesForConflictingNormals()
    {
        var recipes = Recipes([Voice("a.wav"), Voice("c.wav", "Soft", SampleType.Clap)],
            [Voice("b.wav"), Voice("c.wav", "Soft", SampleType.Clap)]);
        var layout = OHsExportLayoutSearch.Find(recipes, 2, default);
        Assert.That(layout.Sum(b => b.Count), Is.EqualTo(4));
        Assert.That(layout.Count, Is.EqualTo(2));
    }

    [Test]
    public void AuthoredSilentNormalCanSupportAdditionWithoutAnotherFile()
    {
        var recipes = Recipes([Voice("zero.wav", gain: 0)], [Voice("c.wav", "Soft", SampleType.Clap)]);
        var layout = OHsExportLayoutSearch.Find(recipes, 1, default);
        Assert.That(layout.Sum(b => b.Count), Is.EqualTo(2));
        Assert.That(layout.Single()[new("Normal", SampleType.Normal)].Sources, Is.Empty);
    }

    [TestCase(1)] [TestCase(2)] [TestCase(3)]
    public void OccupiedNormalFamiliesRespectCapacity(int capacity)
    {
        var recipes = Recipes([Voice("a.wav")], [Voice("b.wav", "Soft")], [Voice("c.wav", "Drum")],
            [Voice("d.wav", "Soft", SampleType.Clap)]);
        if (capacity == 1) Assert.Throws<InvalidOperationException>(() => OHsExportLayoutSearch.Find(recipes, capacity, default));
        else Assert.That(OHsExportLayoutSearch.Find(recipes, capacity, default).Sum(b => b.Count), Is.EqualTo(5));
    }

    [Test]
    public void RecipeOrderAndDuplicationDoNotChangeLayoutOrMutateInputs()
    {
        var recipes = Recipes([Voice("a.wav")], [Voice("b.wav")], [Voice("c.wav", "Soft", SampleType.Clap)]);
        var before = recipes.Select(r => Signature(r.Select(c => c.Slots))).ToArray();
        var first = OHsExportLayoutSearch.Find(recipes, 3, default);
        var signature = Signature(first);
        var repeated = recipes.Reverse().Concat(recipes).ToArray();
        Assert.That(Signature(OHsExportLayoutSearch.Find(repeated, 3, default)), Is.EqualTo(signature));
        OHsExportLayoutSearch.Find(Recipes([Voice("other.wav", "Drum")]), 1, default);
        Assert.That(Signature(first), Is.EqualTo(signature));
        Assert.That(recipes.Select(r => Signature(r.Select(c => c.Slots))), Is.EqualTo(before));
    }

    [Test]
    public void EmptyAndCancelledAttemptsDoNotLeakState()
    {
        Assert.That(OHsExportLayoutSearch.Find([], 1, default), Is.Empty);
        var recipes = Recipes([Voice("a.wav")]);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() => OHsExportLayoutSearch.Find(recipes, 1, cancellation.Token));
        Assert.That(OHsExportLayoutSearch.Find(recipes, 1, default).Sum(b => b.Count), Is.EqualTo(1));
    }

    [Test]
    public void OptimizationProgressCanCancelBeforeResultIsReturned()
    {
        using var cancellation = new CancellationTokenSource();
        var reports = new List<OHsExportProgress>();
        var progress = new InlineProgress<OHsExportProgress>(p => { reports.Add(p); cancellation.Cancel(); });
        var recipes = Recipes([Voice("a.wav")]);
        Assert.Throws<OperationCanceledException>(() => OHsExportLayoutSearch.Find(recipes, 1, cancellation.Token, progress));
        Assert.That(reports, Is.Not.Empty);
        Assert.That(reports.All(p => p.Percent == 35 && p.Phase.Contains("Optimiz", StringComparison.OrdinalIgnoreCase)), Is.True);
        Assert.That(OHsExportLayoutSearch.Find(recipes, 1, default).Sum(b => b.Count), Is.EqualTo(1));
    }

    [Test]
    public void CancellationAfterCandidateDoesNotReturnPartialOrSharedState()
    {
        using var cancellation = new CancellationTokenSource();
        var candidateReported = false;
        var progress = new InlineProgress<OHsExportProgress>(p =>
        {
            if (!p.Phase.Contains("files", StringComparison.Ordinal)) return;
            candidateReported = true;
            cancellation.Cancel();
        });
        var recipes = Recipes([Voice("a.wav")], [Voice("b.wav")]);
        Assert.Throws<OperationCanceledException>(() => OHsExportLayoutSearch.Find(recipes, 2, cancellation.Token, progress));
        Assert.That(candidateReported, Is.True);
        Assert.That(OHsExportLayoutSearch.Find(recipes, 2, default).Sum(b => b.Count), Is.EqualTo(2));
    }
}
