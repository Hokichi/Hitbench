using System.Numerics;
using Hitbench.Core.Entities;
using Hitbench.Resources.DataModels;
using Hitbench.Services;

namespace Hitbench.Test.Services;

public class OHsMidiImportMapperTests
{
    private readonly OHsMidiImportMapper mapper = new();
    internal static OHsMidiImportSource Source(long tick = 1920, bool explicitTempo = true) => new("song.mid", 480, explicitTempo,
        [new(0, 500000, 0)], [new(0, 1, "Piano", [new(60, tick, (BigInteger)tick * 500000)])], 0, []);
    private static UninheritedPoint[] Points(params (int Offset, int Bpm)[] values) => values.Select(p => new UninheritedPoint { Offset = p.Offset, BPM = p.Bpm }).ToArray();

    [TestCase(OHsMidiImportMode.AsIs, 2000)]
    [TestCase(OHsMidiImportMode.ClosestBeat, 2000)]
    [TestCase(OHsMidiImportMode.ProjectBpm, 1333)]
    public void BeatFourUsesSelectedTimingMode(OHsMidiImportMode mode, int expected) =>
        Assert.That(mapper.Map(Source(), Points((0, 180)), 4, 10000, mode, []).Single().Notes.Single().Timestamp, Is.EqualTo(expected));

    [Test]
    public void BeatMappingAccumulatesAcrossTempoChanges() =>
        Assert.That(mapper.Map(Source(), Points((0, 120), (1000, 240)), 4, 10000, OHsMidiImportMode.ProjectBpm, []).Single().Notes.Single().Timestamp, Is.EqualTo(1500));

    [Test]
    public void MidBeatChangesKeepContinuousMusicalPosition()
    {
        Assert.That(mapper.Map(Source(960), Points((0, 120), (750, 240)), 4, 10000, OHsMidiImportMode.ProjectBpm, []).Single().Notes.Single().Timestamp, Is.EqualTo(875));
        Assert.That(mapper.Map(Source(160), Points((0, 180)), 3, 10000, OHsMidiImportMode.ProjectBpm, []).Single().Notes.Single().Timestamp, Is.EqualTo(111));
        Assert.That(mapper.Map(Source(840), Points((0, 120), (750, 240)), 1, 10000, OHsMidiImportMode.ClosestBeat, []).Single().Notes.Single().Timestamp, Is.EqualTo(750));
    }

    [TestCase(-100, -100, true)]
    [TestCase(1000, 1000, true)]
    [TestCase(500, 500, false)]
    public void FinalTimestampsDetermineLocks(int origin, int expected, bool locked)
    {
        var note = mapper.Map(Source(0), Points((origin, 120)), 4, 1000, OHsMidiImportMode.AsIs, []).Single().Notes.Single();
        Assert.That((note.Key, note.Timestamp, note.IsReadOnly), Is.EqualTo(("C4", expected, locked)));
    }

    [TestCase(OHsMidiImportMode.ClosestBeat)]
    [TestCase(OHsMidiImportMode.ProjectBpm)]
    public void SnappingHalfwayChoosesEarlierBeat(OHsMidiImportMode mode) =>
        Assert.That(mapper.Map(Source(240), Points((0, 120)), 1, 1000, mode, []).Single().Notes.Single().Timestamp, Is.Zero);

    [Test]
    public void ClosestBeatDoesNotClampNotesToAudioEnd()
    {
        var note = mapper.Map(Source(), Points((0, 120)), 1, 1000, OHsMidiImportMode.ClosestBeat, []).Single().Notes.Single();
        Assert.That((note.Timestamp, note.IsReadOnly), Is.EqualTo((2000, true)));
    }

    [Test]
    public void FullTimingSequenceMatchesWithoutRounding()
    {
        Assert.That(mapper.MatchesTiming(Source(), Points((200, 120))), Is.True);
        Assert.That(mapper.MatchesTiming(Source(), Points((0, 120), (1000, 120))), Is.False);
        Assert.That(mapper.MatchesTiming(Source(explicitTempo: false), Points((0, 120))), Is.False);
        var fractionalTempo = new OHsMidiImportSource("song.mid", 480, true, [new(0, 499999, 0)], Source().Parts, 0, []);
        Assert.That(mapper.MatchesTiming(fractionalTempo, Points((0, 120))), Is.False);
        var fractionalOffset = new OHsMidiImportSource("song.mid", 480, true, [new(0, 500000, 0), new(1, 250000, 500000)], Source().Parts, 0, []);
        Assert.That(mapper.MatchesTiming(fractionalOffset, Points((0, 120), (1, 240))), Is.False);
        var exactOffset = new OHsMidiImportSource("song.mid", 480, true, [new(0, 500000, 0), new(960, 250000, 480000000)], Source().Parts, 0, []);
        Assert.That(mapper.MatchesTiming(exactOffset, Points((0, 120), (1000, 240))), Is.True);
        Assert.That(mapper.MatchesTiming(exactOffset, Points((0, 120), (1001, 240))), Is.False);
        Assert.That(mapper.MatchesTiming(Source(), Points((0, 180), (0, 120))), Is.True);
    }

    [Test]
    public void RoundingMergesOnlyIdenticalPitchAndTimestamp()
    {
        var source = new OHsMidiImportSource("song.mid", 1000, true, [new(0, 500000, 0)],
            [new(0, 1, "Piano", [new(60, 0, 0), new(60, 1, 1), new(64, 1, 1), new(60, 2, 500000)])], 0, []);
        var notes = mapper.Map(source, Points((0, 120)), 4, 1000, OHsMidiImportMode.AsIs, ["Piano", "Piano (2)"]).Single();
        Assert.That(notes.Name, Is.EqualTo("Piano (1)"));
        Assert.That(notes.Notes.Select(n => (n.Key, n.Timestamp)), Is.EqualTo(new[] { ("C4", 0), ("E4", 0), ("C4", 1) }));
    }

    [TestCase(0)]
    [TestCase(-1)]
    public void InvalidGridIsRejected(int bpm) =>
        Assert.Throws<InvalidDataException>(() => mapper.Map(Source(), Points((0, bpm)), 4, 1000, OHsMidiImportMode.AsIs, []));

    [Test]
    public void OverflowRejectsRatherThanClamps() =>
        Assert.Throws<InvalidDataException>(() => mapper.Map(Source(long.MaxValue), Points((0, 120)), 4, 1000, OHsMidiImportMode.AsIs, []));
}
