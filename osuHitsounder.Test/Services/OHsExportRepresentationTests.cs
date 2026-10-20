using osuHitsounder.Core.Enums;
using osuHitsounder.Resources.DataModels;
using osuHitsounder.Services;
namespace osuHitsounder.Test.Services;

public class OHsExportRepresentationTests
{
    [Test]
    public void EveryPairOfTheTwelveSlotsUsesOnlyNecessaryMixesAndPreservesSingletonIdentity()
    {
        var slots = new[] { "Normal", "Soft", "Drum" }.SelectMany(set => Enum.GetValues<SampleType>().Select(type => (set, type))).ToArray();
        foreach (var a in slots)
            foreach (var b in slots)
            {
                var voices = new[] { Voice(a.set, a.type, "a.wav"), Voice(b.set, b.type, "b.wav") };
                var bothNormal = a.type == SampleType.Normal && b.type == SampleType.Normal;
                var bothAdditional = a.type != SampleType.Normal && b.type != SampleType.Normal;
                var mixes = bothNormal || bothAdditional && (a.set != b.set || a.type == b.type) ? 1 : 0;
                var representations = OHsExportRepresentations.Build(voices, default);
                Assert.That(representations, Is.Not.Empty);
                foreach (var representation in representations)
                {
                    Assert.That(representation.MergeCount, Is.EqualTo(mixes), $"{a}/{b}");
                    foreach (var slot in representation.Slots.Where(p => p.Value.Sources.Count > 0))
                        Assert.That(voices.Any(v => slot.Value.Sources.Any(s => s.FileName == v.FileName) && v.Sampleset == slot.Key.Sampleset && v.Type == slot.Key.Type), Is.True);
                }
            }
    }
    [Test]
    public void MultipleConflictsAndOverflowKeepOtherPlayableSourcesIndependent()
    {
        var simultaneous = OHsExportRepresentations.Build([Voice("Normal", SampleType.Normal, "n.wav"), Voice("Drum", SampleType.Normal, "n2.wav"), Voice("Soft", SampleType.Whistle, "w.wav"), Voice("Drum", SampleType.Clap, "c.wav")], default);
        Assert.That(simultaneous.All(r => r.MergeCount == 2), Is.True);
        var overflow = OHsExportRepresentations.Build([Voice("Normal", SampleType.Normal, "n.wav"), Voice("Soft", SampleType.Whistle, "w.wav"), Voice("Soft", SampleType.Finish, "f.wav"), Voice("Soft", SampleType.Clap, "c.wav"), Voice("Drum", SampleType.Clap, "d.wav")], default);
        Assert.That(overflow.All(r => r.MergeCount == 1 && r.MixedComponentCount == 2), Is.True);
        Assert.That(OHsExportRepresentations.Build([Voice("Soft", SampleType.Clap, "zero.wav", 0)], default).All(r => r.MergeCount == 0), Is.True);
    }
    private static OHsExportVoice Voice(string set, SampleType type, string file = "a.wav", int gain = 100) => new(100, file, gain, set, type, 0);
    [TestCase(false)] [TestCase(true)]
    public void PianoChordKeepsExactSolutionsWithinCancellationBudget(bool additionalType)
    {
        var voices = Enumerable.Range(0, 36).Select(i => Voice("Soft", additionalType && i == 35 ? SampleType.Clap : SampleType.Whistle, $"note{i}.wav")).ToArray();
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        var result = OHsExportRepresentations.Build(voices, cancellation.Token);
        Assert.That(result, Is.Not.Empty);
        Assert.That(result.All(r => r.MergeCount == (additionalType ? 34 : 35)), Is.True);
        Assert.That(result.All(r => r.MixedComponentCount == (additionalType ? 35 : 36)), Is.True);
    }

    [TestCase(0)] [TestCase(1)] [TestCase(2)] [TestCase(3)]
    [TestCase(4)] [TestCase(5)] [TestCase(6)] [TestCase(7)]
    public void AllAdditionMasksRemainIndependent(int mask)
    {
        var voices = new List<OHsExportVoice> { Voice("Drum", SampleType.Normal) };
        if ((mask & 1) != 0) voices.Add(Voice("Soft", SampleType.Whistle, "w.wav"));
        if ((mask & 2) != 0) voices.Add(Voice("Soft", SampleType.Finish, "f.wav"));
        if ((mask & 4) != 0) voices.Add(Voice("Soft", SampleType.Clap, "c.wav"));
        var result = OHsExportRepresentations.Build(voices, default);
        Assert.That(result, Is.Not.Empty);
        Assert.That(result.All(r => r.MergeCount == 0 && r.HitSound == (1 | mask << 1)), Is.True);
    }

    [Test]
    public void AdditionOnlyEnumeratesAllSilentNormalSets()
    {
        var result = OHsExportRepresentations.Build([Voice("Soft", SampleType.Clap)], default);
        Assert.That(result.Select(r => r.NormalSet), Is.EquivalentTo(new[] { "Normal", "Soft", "Drum" }));
        Assert.That(result.All(r => r.Slots[new(r.NormalSet, SampleType.Normal)].Sources.Count == 0), Is.True);
    }

    [Test]
    public void IncompatibleAdditionSetsMixMinimumSubsetAndKeepNormalIndependent()
    {
        var result = OHsExportRepresentations.Build([Voice("Normal", SampleType.Normal), Voice("Soft", SampleType.Whistle, "w.wav"), Voice("Drum", SampleType.Clap, "c.wav")], default);
        Assert.That(result.All(r => r.MergeCount == 1 && r.MixedComponentCount == 2), Is.True);
        Assert.That(result.All(r => r.Slots[new("Normal", SampleType.Normal)].Sources.Count == 1), Is.True);
    }

    [Test]
    public void DuplicateFilesSumGainButDifferentFilesInSameSlotMix()
    {
        var duplicates = OHsExportRepresentations.Build([Voice("Soft", SampleType.Clap, "a.wav", 70), Voice("Soft", SampleType.Clap, "A.wav", 40)], default);
        Assert.That(duplicates.All(r => r.MergeCount == 0), Is.True);
        Assert.That(duplicates[0].Slots[new("Soft", SampleType.Clap)].Sources.Single().GainPercent, Is.EqualTo(110));
        var collision = OHsExportRepresentations.Build([Voice("Soft", SampleType.Clap), Voice("Soft", SampleType.Clap, "b.wav"), Voice("Soft", SampleType.Clap, "c.wav")], default);
        Assert.That(collision.All(r => r.MergeCount == 2), Is.True);
    }

    [Test]
    public void NamesAndRecipeIdentitiesAreCanonical()
    {
        Assert.That(OHsExportRepresentations.FileName(new("Soft", SampleType.Clap), 1, ".ogg"), Is.EqualTo("soft-hitclap.ogg"));
        Assert.That(OHsExportRepresentations.FileName(new("Drum", SampleType.Finish), 999, ".wav"), Is.EqualTo("drum-hitfinish999.wav"));
        Assert.That(OHsExportAsset.Create([("a.wav", 70)]).Id, Is.EqualTo(OHsExportAsset.Create([("A.wav", 70)]).Id));
        Assert.That(OHsExportAsset.Create([("a.wav", 70)]).Id, Is.Not.EqualTo(OHsExportAsset.Create([("a.wav", 40)]).Id));
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() => OHsExportRepresentations.Build([Voice("Normal", SampleType.Normal)], cancellation.Token));
    }
}
