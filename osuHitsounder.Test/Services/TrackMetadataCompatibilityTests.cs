using Newtonsoft.Json.Linq;
using osuHitsounder.Core.Entities;
using osuHitsounder.Core.Enums;
using osuHitsounder.Services.ProjectFiles;

namespace osuHitsounder.Test.Services;

public class TrackMetadataCompatibilityTests
{
    [Test]
    public void LegacyTrackSamplesetDoesNotReplaceAdditionOrControlPointSampleset()
    {
        var project = ProjectDataSerializer.Deserialize("""
            {"Tracks":[{"Sampleset":"Drum","Addition":"Soft"}],
             "ControlPoints":[{"Timestamp":0,"Volume":50,"Sampleset":"Drum"}]}
            """);
        var saved = JObject.Parse(ProjectDataSerializer.Serialize(project));
        Assert.That(saved["Tracks"]![0]!["Sampleset"], Is.Null);
        Assert.That(project.Tracks[0].Addition, Is.EqualTo("Soft"));
        Assert.That(saved["ControlPoints"]![0]!["Sampleset"]!.Value<string>(), Is.EqualTo("Drum"));
        Assert.That(ProjectDataSerializer.Deserialize(saved.ToString()).ControlPoints[0].Sampleset, Is.EqualTo("Drum"));
    }

    [Test]
    public void NewInstrumentDefaultsSurviveRoundTrip()
    {
        var instrument = new InstrumentalTrack();
        Assert.That(instrument.Addition, Is.EqualTo("Soft"));
        var copy = ProjectDataSerializer.Deserialize(ProjectDataSerializer.Serialize(new ProjectData { Tracks = [instrument] })).Tracks[0];
        Assert.That(copy.Addition, Is.EqualTo("Soft"));
        Assert.That(copy.SampleType, Is.EqualTo(SampleType.Whistle));
    }
}
