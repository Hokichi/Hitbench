using System.Text.Json;
using Hitbench.Core.Enums;
using Hitbench.Resources.DataModels;
using Hitbench.Services;

namespace Hitbench.Test.Services;

public class OHsExportReusePerformanceTests
{
    [Test]
    public void NativeFallbackRecipeConflictsFinishAtProvenMinimum()
    {
        var recipes = LoadNativeFallbackRecipes();
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var watch = System.Diagnostics.Stopwatch.StartNew();
        var layout = OHsExportLayoutSearch.Find(recipes, 999, cancellation.Token);
        TestContext.Out.WriteLine($"Exact native-fallback layout: {watch.ElapsedMilliseconds} ms");
        Assert.That(FileCount(layout), Is.EqualTo(37));
        AssertCovers(recipes, layout);
    }

    [Test]
    public void ConditionalBoundsMatchIndependentMinimum()
    {
        var random = new Random(91109);
        var assets = new[] { Asset("a"), Asset("b"), OHsExportAsset.Create([]) };
        for (var iteration = 0; iteration < 40; iteration++)
        {
            var recipes = Enumerable.Range(0, random.Next(1, 5)).Select(_ =>
                (IReadOnlyList<OHsExportRepresentation>)Enumerable.Range(0, random.Next(1, 4)).Select(_ =>
                {
                    var normalSlot = random.Next(3) * 4;
                    var additionSlot = random.Next(3) * 4 + random.Next(1, 4);
                    return Choice((normalSlot, random.Next(3) == 0 ? OHsExportAsset.NativeFallback : assets[random.Next(assets.Length)]),
                        (additionSlot, assets[random.Next(assets.Length)]));
                }).ToArray()).ToArray();
            for (var capacity = 1; capacity <= 3; capacity++) AssertMinimum(recipes, capacity);
        }
    }

    [Test]
    public void CheaperAlternativeRemainsAvailable()
    {
        var a = Asset("a");
        IReadOnlyList<IReadOnlyList<OHsExportRepresentation>> recipes = [
            [Choice((0, OHsExportAsset.NativeFallback), (1, a)),
             Choice((0, OHsExportAsset.NativeFallback), (2, Asset("b")), (3, Asset("c")))],
            [Choice((0, OHsExportAsset.NativeFallback), (1, a))]
        ];
        AssertMinimum(recipes, 1);
        Assert.That(FileCount(OHsExportLayoutSearch.Find(recipes, 1, default)), Is.EqualTo(1));
    }

    [Test]
    public void OptionalIdentityIsCountedOnlyInItsCondition()
    {
        var a = Asset("a");
        IReadOnlyList<IReadOnlyList<OHsExportRepresentation>> recipes = [
            [Choice((0, OHsExportAsset.NativeFallback), (1, Asset("optional"))),
             Choice((0, OHsExportAsset.NativeFallback), (1, a))],
            [Choice((0, OHsExportAsset.NativeFallback), (1, a), (2, Asset("c")))]
        ];
        // At capacity one the optional alternative conflicts, but the reusable alternative fits.
        AssertMinimum(recipes, 1);
        Assert.That(FileCount(OHsExportLayoutSearch.Find(recipes, 1, default)), Is.EqualTo(2));
    }

    [TestCase(false)][TestCase(true)]
    public void NativeMarkersAndAuthoredSilenceRetainDifferentCosts(bool customFallback)
    {
        var addition = new OHsExportVoice(100, "clap.wav", 100, "Soft", SampleType.Clap, 0);
        var silence = new OHsExportVoice(200, "zero.wav", 0, "Normal", SampleType.Normal, 0);
        var recipes = new[] {
            OHsExportRepresentations.Build([addition], default, fallbackPath: customFallback ? "normal.wav" : ""),
            OHsExportRepresentations.Build([silence], default)
        };
        AssertMinimum(recipes, 2);
        Assert.That(FileCount(OHsExportLayoutSearch.Find(recipes, 2, default)), Is.EqualTo(customFallback ? 3 : 2));
    }

    [TestCase(false)][TestCase(true)]
    public void ConditioningPreservesInputsAcrossOrderChangesAndCancelledRetry(bool cancelAtCandidate)
    {
        var a = Asset("a");
        IReadOnlyList<IReadOnlyList<OHsExportRepresentation>> recipes = [
            [Choice((0, OHsExportAsset.NativeFallback), (1, a)), Choice((0, Asset("normal")), (2, a))],
            [Choice((0, OHsExportAsset.NativeFallback), (1, Asset("b")))]
        ];
        var before = string.Join(';', recipes.Select(recipe => Signature(recipe.Select(choice => choice.Slots))));
        using var cancellation = new CancellationTokenSource();
        var cancelled = false;
        var progress = new InlineProgress<OHsExportProgress>(value =>
        {
            if (!cancelAtCandidate || value.Phase.Contains("files", StringComparison.Ordinal))
            { cancelled = true; cancellation.Cancel(); }
        });
        Assert.Throws<OperationCanceledException>(() => OHsExportLayoutSearch.Find(recipes, 3, cancellation.Token, progress));
        Assert.That(cancelled, Is.True);
        AssertMinimum(recipes, 3);
        var first = OHsExportLayoutSearch.Find(recipes, 3, default);
        var reordered = recipes.Reverse().Concat(recipes).ToArray();
        Assert.That(Signature(OHsExportLayoutSearch.Find(reordered, 3, default)), Is.EqualTo(Signature(first)));
        Assert.That(string.Join(';', recipes.Select(recipe => Signature(recipe.Select(choice => choice.Slots)))), Is.EqualTo(before));
    }

    private static OHsExportAsset Asset(string name) => OHsExportAsset.Create([($"{name}.wav", 100)]);

    private static OHsExportRepresentation Choice(params (int Slot, OHsExportAsset Asset)[] entries) =>
        new("Normal", "Normal", 0, entries.ToDictionary(entry =>
            new OHsExportSlot(new[] { "Normal", "Soft", "Drum" }[entry.Slot / 4], (SampleType)(entry.Slot % 4)), entry => entry.Asset), 0, 0);

    private static void AssertMinimum(IReadOnlyList<IReadOnlyList<OHsExportRepresentation>> recipes, int capacity)
    {
        var maps = recipes.Select(recipe => (IReadOnlyList<IReadOnlyDictionary<OHsExportSlot, string>>)recipe.Select(choice =>
            (IReadOnlyDictionary<OHsExportSlot, string>)choice.Slots.ToDictionary(slot => slot.Key, slot => slot.Value.Id)).ToArray()).ToArray();
        var expected = OHsExportBruteForceOracle.MinimumAudioFiles(maps, capacity, new HashSet<string> { OHsExportAsset.NativeFallback.Id });
        if (expected is null)
        {
            Assert.Throws<InvalidOperationException>(() => OHsExportLayoutSearch.Find(recipes, capacity, default));
            return;
        }
        var actual = OHsExportLayoutSearch.Find(recipes, capacity, default);
        Assert.That(FileCount(actual), Is.EqualTo(expected.Value));
        Assert.That(actual.Count, Is.LessThanOrEqualTo(capacity));
        AssertCovers(recipes, actual);
    }

    private static string Signature(IEnumerable<IReadOnlyDictionary<OHsExportSlot, OHsExportAsset>> banks) =>
        string.Join(';', banks.Select(bank => string.Join('|', bank.OrderBy(slot => slot.Key.Sampleset).ThenBy(slot => slot.Key.Type)
            .Select(slot => $"{slot.Key}:{slot.Value.Id}"))).Order(StringComparer.Ordinal));

    private static IReadOnlyList<IReadOnlyList<OHsExportRepresentation>> LoadNativeFallbackRecipes()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "Export", "OHsNativeFallbackReuse.json");
        var matrix = JsonSerializer.Deserialize<Dictionary<string, int[][][]>>(File.ReadAllText(path))!["Recipes"];
        Assert.That(matrix, Has.Length.EqualTo(45));
        var assets = new Dictionary<int, OHsExportAsset>();
        return matrix.Select(recipe => (IReadOnlyList<OHsExportRepresentation>)recipe.Select(choice =>
        {
            Assert.That(choice, Has.Length.EqualTo(12));
            var slots = new Dictionary<OHsExportSlot, OHsExportAsset>();
            for (var slot = 0; slot < choice.Length; slot++)
            {
                var id = choice[slot];
                if (id == 0) continue;
                if (!assets.TryGetValue(id, out var asset))
                    assets[id] = asset = id == -1 ? OHsExportAsset.NativeFallback
                        : new($"fixture-{id:D3}", [($"sample-{id:D3}.wav", 100)]);
                slots.Add(new(new[] { "Normal", "Soft", "Drum" }[slot / 4], (SampleType)(slot % 4)), asset);
            }
            return new OHsExportRepresentation("Normal", "Normal", 0, slots, 0, 0);
        }).ToArray()).ToArray();
    }

    private static int FileCount(IEnumerable<IReadOnlyDictionary<OHsExportSlot, OHsExportAsset>> layout) =>
        layout.Sum(bank => bank.Values.Count(asset => !asset.IsNativeFallback));

    private static void AssertCovers(IReadOnlyList<IReadOnlyList<OHsExportRepresentation>> recipes,
        IReadOnlyList<IReadOnlyDictionary<OHsExportSlot, OHsExportAsset>> layout) =>
        Assert.That(recipes.All(recipe => recipe.Any(choice => layout.Any(bank =>
            choice.Slots.All(slot => bank.TryGetValue(slot.Key, out var asset) && asset.Id == slot.Value.Id)))), Is.True);
}
