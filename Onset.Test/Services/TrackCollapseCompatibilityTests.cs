using Newtonsoft.Json.Linq;
using Onset.Services.ProjectFiles;
namespace Onset.Test.Services;
public sealed class TrackCollapseCompatibilityTests
{
    [Test]
    public void LegacyCollapseMigratesToTracksWithoutRootOutput()
    {
        var project = ProjectDataSerializer.Deserialize("{\"IsCollapsed\":true,\"Tracks\":[{}, {\"IsCollapsed\":false}, {\"Kind\":\"Instrumental\"}]}");
        var saved = JObject.Parse(ProjectDataSerializer.Serialize(project));
        Assert.That(saved.Property("IsCollapsed"), Is.Null);
        Assert.That(saved["Tracks"]!.Select(t => t["IsCollapsed"]!.Value<bool>()), Is.EqualTo(new[] { true, false, true }));
    }
    [TestCase("null")][TestCase("1")][TestCase("\"true\"")][TestCase("[]")]
    public void InvalidTrackCollapseIsRejected(string value) => Assert.Throws<InvalidDataException>(() => ProjectDataSerializer.Deserialize("{\"Tracks\":[{\"IsCollapsed\":" + value + "}]}"));
}
