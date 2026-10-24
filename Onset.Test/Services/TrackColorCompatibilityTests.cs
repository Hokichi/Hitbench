using Onset.Services.ProjectFiles;
namespace Onset.Test.Services;
public class TrackColorCompatibilityTests
{
    [TestCase("{}", null)] [TestCase("{\"Color\":null}", null)] [TestCase("{\"color\":\"#aB12ef\"}", "#AB12EF")]
    public void LoadsLegacyAndNormalizes(string track, string? expected)
    {
        var project=ProjectDataSerializer.Deserialize("{\"Tracks\":["+track+"]}");
        Assert.That(project.Tracks[0].Color, Is.EqualTo(expected));
        Assert.That(ProjectDataSerializer.Deserialize(ProjectDataSerializer.Serialize(project)).Tracks[0].Color, Is.EqualTo(expected));
    }
    [TestCase("12")] [TestCase("\"#FFF\"")] [TestCase("\"red\"")]
    public void RejectsMalformed(string color) => Assert.Throws<InvalidDataException>(()=>ProjectDataSerializer.Deserialize("{\"Tracks\":[{\"Color\":"+color+"}]}"));
}
