using osuHitsounder.Core.Entities;
using osuHitsounder.Services.ProjectFiles;

namespace osuHitsounder.Test.Services;

public class ProjectDataSerializerTests
{
    [Test]
    public void RoundTripsTracksAndSamples()
    {
        var source = new ProjectData { BeatmapPath = "song", Offsets = [125], BPMs = [120],
            Tracks = [new Track { Volume = 75, SelectedSampleFileName = "kick.wav", Samples = [new Sample { FileName = "kick.wav", Volume = 23, Timestamp = 125 }] }] };
        var restored = ProjectDataSerializer.Deserialize(ProjectDataSerializer.Serialize(source));
        Assert.Multiple(() =>
        {
            Assert.That(restored.BeatmapPath, Is.EqualTo("song"));
            Assert.That(restored.Offsets, Is.EqualTo(new[] { 125 }));
            Assert.That(restored.Tracks[0].Volume, Is.EqualTo(75));
            Assert.That(restored.Tracks[0].Samples[0].FileName, Is.EqualTo("kick.wav"));
            Assert.That(restored.Tracks[0].Samples[0].Volume, Is.EqualTo(23));
            Assert.That(restored.Tracks[0].Samples[0].Timestamp, Is.EqualTo(125));
            Assert.That(restored.Tracks[0].SelectedSampleFileName, Is.EqualTo("kick.wav"));
        });
    }

    [Test]
    public void LegacySamplesBecomeTracksWithoutLosingPathOrTimestamps()
    {
        const string json = "{\"BeatmapPath\":\"song\",\"Samples\":[{\"Path\":\"old/kick.wav\",\"Volume\":42,\"Timestamps\":[125]}]}";
        var restored = ProjectDataSerializer.Deserialize(json);
        Assert.That(restored.Tracks, Has.Count.EqualTo(1));
        Assert.That(restored.Tracks[0].Samples[0].FileName, Is.EqualTo("old/kick.wav"));
        Assert.That(restored.Tracks[0].Samples[0].Volume, Is.EqualTo(42));
        Assert.That(restored.Tracks[0].Samples[0].Timestamp, Is.EqualTo(125));
    }

    [Test]
    public void LegacyTrackTimestampArrayExpandsWithoutLosingPlacementOrder()
    {
        const string json = "{\"Tracks\":[{\"Volume\":75,\"Samples\":[{\"FileName\":\"a.wav\",\"Volume\":23,\"Timestamps\":[125,625]}]}]}";

        var restored = ProjectDataSerializer.Deserialize(json);

        Assert.That(restored.Tracks.Single().SelectedSampleFileName, Is.EqualTo("a.wav"));
        Assert.That(restored.Tracks.Single().Samples.Select(sample => sample.Timestamp), Is.EqualTo(new[] { 125, 625 }));
        Assert.That(restored.Tracks.Single().Samples.Select(sample => sample.Volume), Is.All.EqualTo(23));
    }

    [Test]
    public void EmptyLegacyTimestampArrayKeepsSelectionWithoutInventingPlacement()
    {
        const string json = "{\"Tracks\":[{\"Samples\":[{\"FileName\":\"a.wav\",\"Timestamps\":[]}]}]}";
        var restored = ProjectDataSerializer.Deserialize(json);
        Assert.That(restored.Tracks.Single().SelectedSampleFileName, Is.EqualTo("a.wav"));
        Assert.That(restored.Tracks.Single().Samples, Is.Empty);
    }

    [Test]
    public void RejectsMalformedLegacyTimestampCollection()
    {
        const string json = "{\"Tracks\":[{\"Samples\":[{\"FileName\":\"a.wav\",\"Timestamps\":\"125\"}]}]}";
        Assert.Throws<InvalidDataException>(() => ProjectDataSerializer.Deserialize(json));
    }

    [TestCase("{\"Tracks\":{}}")]
    [TestCase("{\"Tracks\":[null]}")]
    [TestCase("{\"Tracks\":[{\"Samples\":null}]}")]
    [TestCase("{\"Tracks\":[{\"Samples\":[null]}]}")]
    public void RejectsInvalidTrackCollections(string json) =>
        Assert.Throws<InvalidDataException>(() => ProjectDataSerializer.Deserialize(json));
}
