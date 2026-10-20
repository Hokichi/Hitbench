using osuHitsounder.Core.Entities;
using osuHitsounder.Core.Enums;
using osuHitsounder.Resources.DataModels;
using osuHitsounder.Services;

namespace osuHitsounder.Test.Services;

public class OHsExportInputTests
{
    private static OHsExportOptions Options(int index = 1) => new("Hitsounds", index, ".ogg", ".ogg", false);
    private static OHsExportBeatmap Map() => OHsExportBeatmap.Parse("osu file format v14\n[TimingPoints]\n123.456,428.571428571429,4,2,4,60,1,0\n200,-50,4,2,4,60,0,1\n");

    [Test]
    public void PreservesPreciseTimingAndRequiredRepeatedControlPoints()
    {
        var project = new ProjectData { ControlPoints = [new() { Timestamp = 0, Volume = 40 }, new() { Timestamp = 250, Volume = 40 }, new() { Timestamp = 300, IsDisabled = true }] };
        var input = OHsExportInputFactory.Create(project, Map(), Options(), default);
        var red = input.RequiredPoints.Single(p => p.Kind == OHsExportTimingKind.SourceRed);
        Assert.That(red.Timestamp, Is.EqualTo(123.456m));
        Assert.That(red.BeatLength, Is.EqualTo("428.571428571429"));
        Assert.That(input.RequiredPoints.Where(p => p.Kind == OHsExportTimingKind.ProjectControl).Select(p => p.Timestamp), Is.EqualTo(new decimal[] { 0, 250 }));
        Assert.That(input.RequiredPoints.Single(p => p.Timestamp == 200).BeatLength, Is.EqualTo("-50"));
        Assert.That(red.Volume, Is.EqualTo(40));
    }

    [Test]
    public void InheritsLookupStateButPreservesExplicitSetAndTrackGain()
    {
        var project = new ProjectData { ControlPoints = [new() { Timestamp = 105, Sampleset = "Drum", Volume = 0 }], Tracks = [new() { Volume = 70, Samples = [new() { FileName = "hit.wav", Timestamp = 100 }] }, new() { Addition = "Soft", SampleType = SampleType.Clap, Samples = [new() { FileName = "clap.wav", Timestamp = 100 }] }] };
        var input = OHsExportInputFactory.Create(project, Map(), Options(), default);
        Assert.That(input.Voices.Select(v => v.Sampleset), Is.EqualTo(new[] { "Drum", "Soft" }));
        Assert.That(input.Voices[0].GainPercent, Is.EqualTo(70));
        Assert.That(input.RequiredPoints.Single(p => p.Timestamp == 105).Volume, Is.Zero);
    }

    [Test]
    public void LastDuplicateDisabledPointBypassesEarlierEntryAndMutedSoloExcludesAll()
    {
        var project = new ProjectData { ControlPoints = [new() { Timestamp = 0, Sampleset = "Soft" }, new() { Timestamp = 0, IsDisabled = true }], Tracks = [new() { IsTrackSoloed = true, IsTrackMuted = true }, new InstrumentalTrack { Notes = [new() { Key = "C4", Timestamp = 100 }] }] };
        var input = OHsExportInputFactory.Create(project, Map(), Options(999), default);
        Assert.That(input.Voices, Is.Empty);
        Assert.That(input.RequiredPoints.Any(p => p.Kind == OHsExportTimingKind.ProjectControl), Is.False);
    }

    [Test]
    public void InstrumentalNotesResolveMappedFilesAndZeroGainKeepsTimestamp()
    {
        var track = new InstrumentalTrack { Volume = 0, KeyFiles = new() { ["C4"] = "piano/C4.wav" }, Notes = [new() { Key = "C4", Timestamp = 100 }] };
        var project = new ProjectData { Tracks = [track] };
        var voice = OHsExportInputFactory.Create(project, Map(), Options(), default).Voices.Single();
        Assert.That((voice.Timestamp, voice.FileName, voice.GainPercent, voice.Sampleset, voice.Type), Is.EqualTo((100, "piano/C4.wav", 0, "Soft", SampleType.Whistle)));
        track.KeyFiles.Clear();
        Assert.Throws<InvalidDataException>(() => OHsExportInputFactory.Create(project, Map(), Options(), default));
    }

    [TestCase(0)]
    [TestCase(1000)]
    public void InvalidIndexFails(int index) => Assert.Throws<ArgumentException>(() => Options(index).Validate());

    [TestCase(-1, 100, "hit.wav", "Normal", SampleType.Normal)]
    [TestCase(0, 101, "hit.wav", "Normal", SampleType.Normal)]
    [TestCase(0, 100, "../hit.wav", "Normal", SampleType.Normal)]
    [TestCase(0, 100, "hit.wav", "Unknown", SampleType.Normal)]
    [TestCase(0, 100, "hit.wav", "Normal", (SampleType)99)]
    public void InvalidIncludedVoiceFails(int timestamp, int gain, string file, string set, SampleType type)
    {
        var project = new ProjectData { Tracks = [new() { Volume = gain, Addition = set, SampleType = type, Samples = [new() { Timestamp = timestamp, FileName = file }] }] };
        Assert.Throws<InvalidDataException>(() => OHsExportInputFactory.Create(project, Map(), Options(), default));
    }
}
