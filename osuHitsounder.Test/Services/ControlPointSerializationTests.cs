using osuHitsounder.Core.Entities;
using osuHitsounder.Services.ProjectFiles;

namespace osuHitsounder.Test.Services;

public class ControlPointSerializationTests
{
    [Test]
    public void DisabledStateRoundTripsAndMissingDefaultsFalse()
    {
        var project = ProjectDataSerializer.Deserialize("{\"ControlPoints\":[{\"Timestamp\":0},{\"Timestamp\":900,\"Volume\":0,\"Sampleset\":\"Drum\",\"IsDisabled\":true}]}");
        Assert.That(project.ControlPoints.Select(p => p.IsDisabled), Is.EqualTo(new[] { false, true }));
        var point = ProjectDataSerializer.Deserialize(ProjectDataSerializer.Serialize(project)).ControlPoints[1];
        Assert.That((point.Timestamp, point.Volume, point.Sampleset, point.IsDisabled), Is.EqualTo((900, 0, "Drum", true)));
    }

    [TestCase("null")][TestCase("\"true\"")][TestCase("1")]
    public void InvalidDisabledFlagIsRejected(string value) =>
        Assert.Throws<InvalidDataException>(() => ProjectDataSerializer.Deserialize("{\"ControlPoints\":[{\"Timestamp\":0,\"IsDisabled\":" + value + "}]}"));

    [Test]
    public void DuplicateDisabledAliasesAreRejected() => Assert.Throws<InvalidDataException>(() =>
        ProjectDataSerializer.Deserialize("{\"ControlPoints\":[{\"Timestamp\":0,\"IsDisabled\":true,\"isdisabled\":false}]}"));

    [TestCase("{}")]
    [TestCase("{\"ControlPoints\":null}")]
    public void MissingPointsAreEmpty(string json) => Assert.That(ProjectDataSerializer.Deserialize(json).ControlPoints, Is.Empty);

    [TestCase("Normal")][TestCase("Soft")][TestCase("Drum")]
    public void RoundTripPreservesExplicitZero(string sampleset)
    {
        var p = new ProjectData { ControlPoints = [new() { Timestamp = 900, Volume = 0, Sampleset = sampleset }] };
        var point = ProjectDataSerializer.Deserialize(ProjectDataSerializer.Serialize(p)).ControlPoints.Single();
        Assert.That((point.Timestamp, point.Volume, point.Sampleset), Is.EqualTo((900, 0, sampleset)));
    }

    [Test]
    public void LegacyDefaultsAndOrderingArePreserved()
    {
        var p = ProjectDataSerializer.Deserialize("{\"ControlPoints\":[{\"Timestamp\":1000},{\"timestamp\":900,\"sampleset\":null},{\"Timestamp\":1000,\"Volume\":0}]}");
        Assert.That(p.ControlPoints.Select(x => x.Timestamp), Is.EqualTo(new[] { 1000, 900, 1000 }));
        Assert.That(p.ControlPoints.Select(x => x.Volume), Is.EqualTo(new[] { 100, 100, 0 }));
        Assert.That(p.ControlPoints.Select(x => x.Sampleset), Is.All.EqualTo("Normal"));
    }

    [TestCase("{}")][TestCase("{\"Timestamp\":-1}")][TestCase("{\"Timestamp\":2147483648}")]
    [TestCase("{\"Timestamp\":0.5}")][TestCase("{\"Timestamp\":0,\"Volume\":null}")]
    [TestCase("{\"Timestamp\":0,\"Volume\":101}")][TestCase("{\"Timestamp\":0,\"Volume\":-1}")]
    [TestCase("{\"Timestamp\":0,\"Volume\":50.5}")][TestCase("{\"Timestamp\":0,\"Sampleset\":false}")]
    [TestCase("{\"Timestamp\":0,\"Sampleset\":\"Unknown\"}")][TestCase("{\"Timestamp\":0,\"timestamp\":1}")]
    public void InvalidPointsAreRejected(string point) => Assert.Throws<InvalidDataException>(() => ProjectDataSerializer.Deserialize("{\"ControlPoints\":[" + point + "]}"));

    [TestCase("false")][TestCase("{}")][TestCase("[null]")]
    public void InvalidCollectionIsRejected(string value) => Assert.Throws<InvalidDataException>(() => ProjectDataSerializer.Deserialize("{\"ControlPoints\":" + value + "}"));
}
