using Hitbench.Core.Entities;
using Hitbench.Services.ProjectFiles;

namespace Hitbench.Test.Services;

public class InstrumentalProjectSerializerTests
{
    [Test]
    public void MixedTracksRoundTrip()
    {
        var project = new ProjectData
        {
            Tracks = [new Track(), new InstrumentalTrack { Name="Piano", Volume=75,
            KeyFiles=new() { ["C#4"]="piano.wav" }, Notes=[new() { Key="C#4", Timestamp=125}] }]
        };
        var copy = ProjectDataSerializer.Deserialize(ProjectDataSerializer.Serialize(project));
        Assert.That(copy.Tracks[0], Is.TypeOf<Track>());
        Assert.That(copy.Tracks[1], Is.TypeOf<InstrumentalTrack>());
        var instrument = (InstrumentalTrack)copy.Tracks[1];
        Assert.That(instrument.Notes.Single().Timestamp, Is.EqualTo(125));
        Assert.That(instrument.KeyFiles["C#4"], Is.EqualTo("piano.wav"));
        Assert.That(instrument.Volume, Is.EqualTo(75));
    }
    [TestCase("{\"Kind\":\"Other\"}")]
    [TestCase("{\"Kind\":\"Instrumental\",\"Notes\":[{\"Key\":\"C0\",\"Timestamp\":0}]}")]
    [TestCase("{\"Kind\":\"Instrumental\",\"Notes\":[{\"Key\":\"C4\",\"Timestamp\":-1}]}")]
    [TestCase("{\"Kind\":\"Instrumental\",\"Notes\":[{\"Key\":\"C4\",\"Timestamp\":1.5}]}")]
    [TestCase("{\"Kind\":\"Instrumental\",\"KeyFiles\":null}")]
    [TestCase("{\"Kind\":\"Instrumental\",\"Notes\":null}")]
    [TestCase("{\"Kind\":\"Instrumental\",\"Notes\":[{\"Key\":\"C4\",\"Timestamp\":9223372036854775808}]}")]
    [TestCase("{\"Kind\":\"Instrumental\",\"Notes\":[{\"Key\":\"C4\",\"Timestamp\":0},{\"Key\":\"C4\",\"Timestamp\":0}]}")]
    [TestCase("{\"Kind\":\"Instrumental\",\"Notes\":[],\"notes\":[]}")]
    [TestCase("{\"Kind\":\"Instrumental\",\"Samples\":[{\"Timestamp\":0}]}")]
    [TestCase("{\"Kind\":\"Instrumental\",\"KeyFiles\":{\"C4\":\"../file.wav\"}}")]
    public void MalformedInstrumentIsRejected(string track) => Assert.Throws<InvalidDataException>(() => ProjectDataSerializer.Deserialize("{\"Tracks\":[" + track + "]}"));
    [Test]
    public void MetadataIsFixedAndLegacyTracksRemainSamples()
    {
        var t = ProjectDataSerializer.Deserialize("{\"Tracks\":[{\"Kind\":\"Instrumental\",\"Sampleset\":false,\"SampleType\":99}]}").Tracks[0];
        Assert.That(t.Addition, Is.EqualTo("Soft"));
        Assert.That(t.SampleType, Is.EqualTo(Hitbench.Core.Enums.SampleType.Whistle));
        Assert.That(ProjectDataSerializer.Deserialize("{\"Tracks\":[{}]}").Tracks[0], Is.TypeOf<Track>());
    }
}
