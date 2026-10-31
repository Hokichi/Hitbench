using Hitbench.Services.ProjectFiles;
namespace Hitbench.Test.Services;
public class TrackColorCompatibilityTests
{
    [TestCase("{\"Kind\":\"Instrumental\"}", null)] [TestCase("{\"Color\":\"#FFFFFF\"}", "#FFFFFF")] [TestCase("{\"Color\":\"#000000\"}", "#000000")]
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
