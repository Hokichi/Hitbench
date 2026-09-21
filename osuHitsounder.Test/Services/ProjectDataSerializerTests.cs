using osuHitsounder.Core.Entities;
using osuHitsounder.Services.ProjectFiles;

namespace osuHitsounder.Test.Services;

public class ProjectDataSerializerTests
{
    [Test]
    public void RoundTripsTracksAndSamples()
    {
        var source = new ProjectData { BeatmapPath = "song", Offsets = [125], BPMs = [120],
            Tracks = [new Track { Volume = 75, Samples = [new Sample { FileName = "kick.wav", Volume = 23, Timestamps = [125, 625] }] }] };
        var restored = ProjectDataSerializer.Deserialize(ProjectDataSerializer.Serialize(source));
        Assert.Multiple(() =>
        {
            Assert.That(restored.BeatmapPath, Is.EqualTo("song"));
            Assert.That(restored.Offsets, Is.EqualTo(new[] { 125 }));
            Assert.That(restored.Tracks[0].Volume, Is.EqualTo(75));
            Assert.That(restored.Tracks[0].Samples[0].FileName, Is.EqualTo("kick.wav"));
            Assert.That(restored.Tracks[0].Samples[0].Volume, Is.EqualTo(23));
            Assert.That(restored.Tracks[0].Samples[0].Timestamps, Is.EqualTo(new[] { 125, 625 }));
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
        Assert.That(restored.Tracks[0].Samples[0].Timestamps, Is.EqualTo(new[] { 125 }));
    }

    [TestCase("{\"Tracks\":{}}")]
    [TestCase("{\"Tracks\":[null]}")]
    [TestCase("{\"Tracks\":[{\"Samples\":null}]}")]
    [TestCase("{\"Tracks\":[{\"Samples\":[null]}]}")]
    public void RejectsInvalidTrackCollections(string json) =>
        Assert.Throws<InvalidDataException>(() => ProjectDataSerializer.Deserialize(json));
}
