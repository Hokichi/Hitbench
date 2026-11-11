using System.Numerics;
using Melanchall.DryWetMidi.Core;
using Hitbench.Services.FileSystem;

namespace Hitbench.Test.Services;

public class OHsMidiFileReaderTests
{
    [Test]
    public void FormatZeroSeparatesChannelsWithoutInventingExplicitTempo()
    {
        using var stream = OHsMidiTestData.File(480, OHsMidiTestData.Track(
            new NoteOnEvent((Melanchall.DryWetMidi.Common.SevenBitNumber)60, (Melanchall.DryWetMidi.Common.SevenBitNumber)100) { DeltaTime = 480 },
            new NoteOnEvent((Melanchall.DryWetMidi.Common.SevenBitNumber)64, (Melanchall.DryWetMidi.Common.SevenBitNumber)100) { Channel = (Melanchall.DryWetMidi.Common.FourBitNumber)1 },
            new NoteOnEvent((Melanchall.DryWetMidi.Common.SevenBitNumber)67, (Melanchall.DryWetMidi.Common.SevenBitNumber)0)));
        var source = new OHsMidiFileReader().Read(stream, "song.mid", default);
        Assert.That(source.HasExplicitTempo, Is.False);
        Assert.That(source.Parts.Select(p => (p.TrackIndex, p.Channel)), Is.EqualTo(new[] { (0, 1), (0, 2) }));
        Assert.That(source.Parts[0].Attacks.Single().ElapsedMicrosecondsNumerator, Is.EqualTo(new BigInteger(240000000)));
        Assert.That(stream.CanRead, Is.True);
    }

    [Test]
    public void KeepsRawTempoResetsAndSourceNamesAndCountsExclusions()
    {
        using var stream = OHsMidiTestData.File(480,
            OHsMidiTestData.Track(new SetTempoEvent(500000), new SetTempoEvent(500000) { DeltaTime = 480 }),
            OHsMidiTestData.Track(new SequenceTrackNameEvent("音楽"), Note(60), Note(23), Note(64, 9)));
        var source = new OHsMidiFileReader().Read(stream, "song.mid", default);
        Assert.That(source.Tempos.Select(t => t.Tick), Is.EqualTo(new long[] { 0, 480 }));
        Assert.That(source.Parts.Single().Name, Is.EqualTo("音楽"));
        Assert.That(source.Parts.Single().TrackIndex, Is.EqualTo(1));
        Assert.That(source.ExcludedPitchCount, Is.EqualTo(1));
        Assert.That(source.ExcludedPercussionParts, Has.Count.EqualTo(1));
    }

    [Test]
    public void ConflictingGlobalTempoIsRejected()
    {
        using var stream = OHsMidiTestData.File(480, OHsMidiTestData.Track(new SetTempoEvent(500000), Note(60)), OHsMidiTestData.Track(new SetTempoEvent(400000)));
        Assert.Throws<InvalidDataException>(() => new OHsMidiFileReader().Read(stream, "song.mid", default));
    }

    [Test]
    public void EmptyAndCorruptedAndNoAttacksAreRejected()
    {
        using var empty = new MemoryStream();
        using var corrupt = new MemoryStream([1, 2, 3, 4, 5]);
        using var metadata = OHsMidiTestData.File(480, OHsMidiTestData.Track(new SetTempoEvent(500000)));
        foreach (var stream in new[] { empty, corrupt, metadata })
            Assert.Throws<InvalidDataException>(() => new OHsMidiFileReader().Read(stream, "bad.mid", default));
    }

    [Test]
    public void CancellationIsObserved()
    {
        using var stream = OHsMidiTestData.File(480, OHsMidiTestData.Track(Note(60)));
        Assert.Throws<OperationCanceledException>(() => new OHsMidiFileReader().Read(stream, "song.mid", new(true)));
    }

    private static NoteOnEvent Note(byte pitch, byte channel = 0) => new((Melanchall.DryWetMidi.Common.SevenBitNumber)pitch, (Melanchall.DryWetMidi.Common.SevenBitNumber)100) { Channel = (Melanchall.DryWetMidi.Common.FourBitNumber)channel };

    [TestCase(2, 1, 224)]
    [TestCase(1, 231, 40)]
    public void UnsupportedFormatAndSmpteAreDistinctFromCorruption(byte format, byte high, byte low)
    {
        using var original = OHsMidiTestData.File(480, OHsMidiTestData.Track(Note(60)));
        var bytes = original.ToArray(); bytes[9] = format; bytes[12] = high; bytes[13] = low;
        using var stream = new MemoryStream(bytes);
        Assert.Throws<NotSupportedException>(() => new OHsMidiFileReader().Read(stream, "song.mid", default));
    }

    [Test]
    public void ImmutableSourceCopiesCollections()
    {
        var attacks = new List<Hitbench.Resources.DataModels.OHsMidiAttack> { new(60, 0, 0) };
        var part = new Hitbench.Resources.DataModels.OHsMidiPart(0, 1, "name", attacks);
        attacks.Clear();
        Assert.That(part.Attacks, Has.Count.EqualTo(1));
    }

    [Test]
    public void MidiTwoClipIsUnsupported()
    {
        using var stream = new MemoryStream(System.Text.Encoding.ASCII.GetBytes("SMF2CLIP"));
        Assert.Throws<NotSupportedException>(() => new OHsMidiFileReader().Read(stream, "clip.mid", default));
    }

    [Test]
    public void LaterTempoUsesMidiDefaultBeforeItsFirstEvent()
    {
        using var stream = OHsMidiTestData.File(480, OHsMidiTestData.Track(Note(60), new SetTempoEvent(250000) { DeltaTime = 480 }, Note(64)));
        var source = new OHsMidiFileReader().Read(stream, "song.mid", default);
        Assert.That(source.HasExplicitTempo, Is.True);
        Assert.That(source.Tempos.Select(t => t.MicrosecondsPerQuarterNote), Is.EqualTo(new[] { 500000, 250000 }));
        Assert.That(source.Parts.Single().Attacks.Last().ElapsedMicrosecondsNumerator, Is.EqualTo(new BigInteger(240000000)));
    }

    [TestCase(0, false)]
    [TestCase(480, true)]
    public void InvalidResolutionAndTruncationAreRejected(int ppq, bool truncate)
    {
        using var original = OHsMidiTestData.File(480, OHsMidiTestData.Track(Note(60)));
        var bytes = original.ToArray();
        if (truncate) Array.Resize(ref bytes, bytes.Length - 2);
        else { bytes[12] = (byte)(ppq >> 8); bytes[13] = (byte)ppq; }
        using var stream = new MemoryStream(bytes);
        Assert.Throws<InvalidDataException>(() => new OHsMidiFileReader().Read(stream, "bad.mid", default));
    }
}
