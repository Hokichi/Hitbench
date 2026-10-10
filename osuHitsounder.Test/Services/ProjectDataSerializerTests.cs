using osuHitsounder.Core.Entities;
using osuHitsounder.Services.ProjectFiles;

namespace osuHitsounder.Test.Services;

public class ProjectDataSerializerTests
{
    [TestCase("{\"Tracks\":[{}]}")]
    [TestCase("{\"Tracks\":[{\"Sampleset\":null,\"Addition\":null}]}")]
    [TestCase("{\"Samples\":[{\"Path\":\"old.wav\",\"Timestamps\":[0]}]}")]
    public void MetadataDefaultsForOldAndNullFields(string json)
    {
        var track = ProjectDataSerializer.Deserialize(json).Tracks.Single();
        Assert.That(track.Sampleset, Is.EqualTo("Normal"));
        Assert.That(track.Addition, Is.EqualTo("None"));
    }

    [TestCase("Normal", "None")]
    [TestCase("Soft", "Normal")]
    [TestCase("Drum", "Soft")]
    [TestCase("Normal", "Drum")]
    public void MetadataDefaultsAndRoundTripsAllChoices(string sampleset, string addition)
    {
        var project = new ProjectData { Tracks = [new Track { Sampleset = sampleset, Addition = addition,
            Name = "Kick", SelectedSampleFileName = "kick.wav", Volume = 75,
            Samples = [new Sample { FileName = "kick.wav", Timestamp = 125, Volume = 23 }] }] };
        var track = ProjectDataSerializer.Deserialize(ProjectDataSerializer.Serialize(project)).Tracks.Single();
        Assert.That(track.Sampleset, Is.EqualTo(sampleset));
        Assert.That(track.Addition, Is.EqualTo(addition));
        Assert.That(track.Name, Is.EqualTo("Kick"));
        Assert.That(track.SelectedSampleFileName, Is.EqualTo("kick.wav"));
        Assert.That(track.Volume, Is.EqualTo(75));
        Assert.That(track.Samples.Single().Timestamp, Is.EqualTo(125));
        Assert.That(track.Samples.Single().Volume, Is.EqualTo(23));
    }

    [TestCase("\"None\"")]
    [TestCase("\"unknown\"")]
    [TestCase("123")]
    [TestCase("true")]
    [TestCase("{}")]
    [TestCase("[]")]
    public void MetadataRejectsMalformedValuesEvenWithoutSamples(string value)
    {
        Assert.Throws<InvalidDataException>(() => ProjectDataSerializer.Deserialize("{\"Tracks\":[{\"Sampleset\":" + value + "}]}"));
        if (value != "\"None\"")
            Assert.Throws<InvalidDataException>(() => ProjectDataSerializer.Deserialize("{\"Tracks\":[{\"Addition\":" + value + ",\"Samples\":[]}]}"));
    }

    [Test]
    public void TrackNamesDefaultEmptyAndRoundTripIndependentlyOfSample()
    {
        Assert.That(new Track().Name, Is.Empty);
        var name = "กลอง " + new string('a', 1024);
        var project = new ProjectData { Tracks = [new Track { Name = name, SelectedSampleFileName = "kick.wav" },
            new Track { Name = name }, new Track()] };
        var restored = ProjectDataSerializer.Deserialize(ProjectDataSerializer.Serialize(project));
        Assert.That(restored.Tracks.Select(track => track.Name), Is.EqualTo(new[] { name, name, "" }));
        Assert.That(restored.Tracks[0].SelectedSampleFileName, Is.EqualTo("kick.wav"));
    }

    [TestCase("{\"Tracks\":[{}]}")]
    [TestCase("{\"Tracks\":[{\"Name\":null}]}")]
    [TestCase("{\"Samples\":[{\"Path\":\"old.wav\",\"Timestamps\":[0]}]}")]
    public void OlderProjectsAndNullNamesLoadAsEmpty(string json) =>
        Assert.That(ProjectDataSerializer.Deserialize(json).Tracks.Single().Name, Is.Empty);

    [TestCase("123")]
    [TestCase("true")]
    [TestCase("{}")]
    [TestCase("[]")]
    public void RejectsNonStringTrackNames(string token)
    {
        var error = Assert.Throws<InvalidDataException>(() =>
            ProjectDataSerializer.Deserialize("{\"Tracks\":[{\"Name\":" + token + "}]}"));
        Assert.That(error!.Message, Is.EqualTo("Track Name must be a string or null."));
    }

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
