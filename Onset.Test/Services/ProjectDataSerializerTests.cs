using Onset.Core.Entities;
using Onset.Services.ProjectFiles;

namespace Onset.Test.Services;

public class ProjectDataSerializerTests
{
    [TestCase("0")]
    [TestCase("\"obsolete\"")]
    public void LegacyNodeVolumesAreIgnoredAndTrackAndControlVolumesSurvive(string volume)
    {
        var json = "{\"Tracks\":[{\"Volume\":70,\"Samples\":[{\"FileName\":\"hit.wav\",\"Timestamp\":125,\"Volume\":" + volume + "}]},{\"Kind\":\"Instrumental\",\"Volume\":70,\"Notes\":[{\"Key\":\"C4\",\"Timestamp\":125,\"Volume\":" + volume + "}]}],\"ControlPoints\":[{\"Timestamp\":0,\"Volume\":40}]}";
        var saved = Newtonsoft.Json.Linq.JObject.Parse(ProjectDataSerializer.Serialize(ProjectDataSerializer.Deserialize(json)));
        Assert.That(saved.SelectTokens("Tracks[*].Samples[*].Volume"), Is.Empty);
        Assert.That(saved.SelectTokens("Tracks[*].Notes[*].Volume"), Is.Empty);
        Assert.That(saved.SelectTokens("Tracks[*].Volume").Select(t => (int)t), Is.All.EqualTo(70));
        Assert.That((int)saved["ControlPoints"]![0]!["Volume"]!, Is.EqualTo(40));
    }

    [TestCase(0)]
    [TestCase(1)]
    [TestCase(2)]
    [TestCase(3)]
    public void SampleTypesRoundTrip(int type)
    {
        var original = new Track { SampleType = (Onset.Core.Enums.SampleType)type };
        var restored = ProjectDataSerializer.Deserialize(ProjectDataSerializer.Serialize(new ProjectData { Tracks = [original] })).Tracks.Single();
        Assert.That(restored.SampleType, Is.EqualTo(original.SampleType));
        Assert.That(ProjectDataSerializer.Deserialize("{\"Tracks\":[{\"SampleType\":null}]}").Tracks.Single().SampleType, Is.EqualTo(Onset.Core.Enums.SampleType.Normal));
    }
    [TestCase("99")]
    [TestCase("true")]
    public void InvalidSampleTypeIsRejected(string token) =>
        Assert.Throws<InvalidDataException>(() => ProjectDataSerializer.Deserialize("{\"Tracks\":[{\"SampleType\":" + token + "}]}"));

    [TestCase(false, false)]
    [TestCase(false, true)]
    [TestCase(true, false)]
    [TestCase(true, true)]
    public void TrackFlagsRoundTripAllCombinations(bool muted, bool soloed)
    {
        var project = new ProjectData { Tracks = [new Track { IsTrackMuted = muted, IsTrackSoloed = soloed,
            Name = "Kick", Volume = 70, Samples = [new Sample { FileName = "kick.wav", Timestamp = 125 }] }] };
        var track = ProjectDataSerializer.Deserialize(ProjectDataSerializer.Serialize(project)).Tracks.Single();
        Assert.That(track.IsTrackMuted, Is.EqualTo(muted));
        Assert.That(track.IsTrackSoloed, Is.EqualTo(soloed));
        Assert.That(track.Name, Is.EqualTo("Kick"));
        Assert.That(track.Volume, Is.EqualTo(70));
        Assert.That(track.Samples.Single().Timestamp, Is.EqualTo(125));
    }

    [TestCase("{\"Tracks\":[{}]}", false, false)]
    [TestCase("{\"Tracks\":[{\"IsTrackMuted\":true}]}", true, false)]
    [TestCase("{\"Tracks\":[{\"IsTrackSoloed\":true}]}", false, true)]
    [TestCase("{\"Samples\":[{\"Path\":\"old.wav\",\"Timestamps\":[0]}]}", false, false)]
    public void OlderAndLegacyProjectsDefaultTrackFlagsToFalse(string json, bool muted, bool soloed)
    {
        var track = ProjectDataSerializer.Deserialize(json).Tracks.Single();
        Assert.That(track.IsTrackMuted, Is.EqualTo(muted));
        Assert.That(track.IsTrackSoloed, Is.EqualTo(soloed));
    }

    [TestCase("null")]
    [TestCase("\"true\"")]
    [TestCase("1")]
    [TestCase("[]")]
    [TestCase("{}")]
    public void TrackFlagsRejectNonBooleanJson(string token)
    {
        foreach (var property in new[] { "IsTrackMuted", "IsTrackSoloed", "isTrackMuted", "istracksoloed" })
            Assert.Throws<InvalidDataException>(() =>
                ProjectDataSerializer.Deserialize("{\"Tracks\":[{\"" + property + "\":" + token + "}]}"));
    }

    [TestCase("{\"Tracks\":[{}]}")]
    [TestCase("{\"Tracks\":[{\"Sampleset\":null,\"Addition\":null}]}")]
    [TestCase("{\"Samples\":[{\"Path\":\"old.wav\",\"Timestamps\":[0]}]}")]
    public void MetadataDefaultsForOldAndNullFields(string json)
    {
        var track = ProjectDataSerializer.Deserialize(json).Tracks.Single();
        Assert.That(track.Addition, Is.EqualTo("None"));
    }

    [TestCase("Normal", "None")]
    [TestCase("Soft", "Normal")]
    [TestCase("Drum", "Soft")]
    [TestCase("Normal", "Drum")]
    public void MetadataDefaultsAndRoundTripsAllChoices(string sampleset, string addition)
    {
        var project = new ProjectData { Tracks = [new Track { Addition = addition,
            Name = "Kick", SelectedSampleFileName = "kick.wav", Volume = 75,
            Samples = [new Sample { FileName = "kick.wav", Timestamp = 125}] }]
        };
        var track = ProjectDataSerializer.Deserialize(ProjectDataSerializer.Serialize(project)).Tracks.Single();
        Assert.That(track.Addition, Is.EqualTo(addition));
        Assert.That(track.Name, Is.EqualTo("Kick"));
        Assert.That(track.SelectedSampleFileName, Is.EqualTo("kick.wav"));
        Assert.That(track.Volume, Is.EqualTo(75));
        Assert.That(track.Samples.Single().Timestamp, Is.EqualTo(125));
    }

    [TestCase("\"None\"")]
    [TestCase("\"unknown\"")]
    [TestCase("123")]
    [TestCase("true")]
    [TestCase("{}")]
    [TestCase("[]")]
    public void MetadataRejectsMalformedValuesEvenWithoutSamples(string value)
    {
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
        var source = new ProjectData
        {
            BeatmapPath = "song",
            Offsets = [125],
            BPMs = [120],
            Tracks = [new Track { Volume = 75, SelectedSampleFileName = "kick.wav", Samples = [new Sample { FileName = "kick.wav", Timestamp = 125 }] }]
        };
        var restored = ProjectDataSerializer.Deserialize(ProjectDataSerializer.Serialize(source));
        Assert.Multiple(() =>
        {
            Assert.That(restored.BeatmapPath, Is.EqualTo("song"));
            Assert.That(restored.Offsets, Is.EqualTo(new[] { 125 }));
            Assert.That(restored.Tracks[0].Volume, Is.EqualTo(75));
            Assert.That(restored.Tracks[0].Samples[0].FileName, Is.EqualTo("kick.wav"));
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
        Assert.That(restored.Tracks[0].Samples[0].Timestamp, Is.EqualTo(125));
    }

    [Test]
    public void LegacyTrackTimestampArrayExpandsWithoutLosingPlacementOrder()
    {
        const string json = "{\"Tracks\":[{\"Volume\":75,\"Samples\":[{\"FileName\":\"a.wav\",\"Volume\":23,\"Timestamps\":[125,625]}]}]}";

        var restored = ProjectDataSerializer.Deserialize(json);

        Assert.That(restored.Tracks.Single().SelectedSampleFileName, Is.EqualTo("a.wav"));
        Assert.That(restored.Tracks.Single().Samples.Select(sample => sample.Timestamp), Is.EqualTo(new[] { 125, 625 }));
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
