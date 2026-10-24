using Newtonsoft.Json.Linq;
using Hitbench.Core.Entities;
using Hitbench.Services.FileSystem;
using Hitbench.Services.ProjectFiles;

namespace Hitbench.Test.Services;

public class ProjectHistoryMetadataTests
{
    [Test]
    public async Task NullLegacyMapsetPathRemainsSearchable()
    {
        var root = Directory.CreateTempSubdirectory("ohs-null-mapset-");
        try
        {
            var folder = root.CreateSubdirectory("projects/Missing");
            await File.WriteAllTextAsync(Path.Combine(folder.FullName, "project.json"), "{\"BeatmapPath\":null}");
            var snapshot = await new HomeFolderService(root.FullName).LoadProjectsAsync(default);
            Assert.That(snapshot.FilterProjects("missing").Count, Is.EqualTo(1));
            Assert.That(snapshot.FilterProjects("not-present").Count, Is.Zero);
        }
        finally { root.Delete(true); }
    }

    [Test, SetCulture("fr-FR")]
    public void TimestampParsingPreservesOffsetIndependentlyOfCulture()
    {
        var project = ProjectDataSerializer.Deserialize("{\"LastChanged\":\"2026-10-18T08:00:00+07:00\"}");
        Assert.That(project.LastChanged, Is.EqualTo(new DateTimeOffset(2026, 10, 18, 1, 0, 0, TimeSpan.Zero)));
        Assert.That(project.LastChanged.Offset, Is.EqualTo(TimeSpan.Zero));
    }

    [Test]
    public void SnapshotOverrideAndAnywhereSearchKeepSourceMetadata()
    {
        var instant = DateTimeOffset.Parse("2026-10-18T01:00:00Z");
        var project = new ProjectData();
        var stored = ProjectDataSerializer.Deserialize(ProjectDataSerializer.Serialize(project, instant));
        Assert.That(stored.LastChanged, Is.EqualTo(instant));
        Assert.That(project.LastChanged, Is.EqualTo(default(DateTimeOffset)));
        var pin = new Hitbench.Resources.DataModels.FolderEntry("pin", "midMATCHend", default) { IsPinned = true };
        var recent = new Hitbench.Resources.DataModels.FolderEntry("recent", "recent", default)
            { LastChanged = instant, MapsetName = "some match mapset" };
        var snapshot = Hitbench.Resources.DataModels.FolderSnapshot.ForProjects([recent, pin], true);
        Assert.That(snapshot.ReadPage(0, 10), Is.EqualTo(new[] { pin, recent }));
        Assert.That(snapshot.FilterProjects(" match ").ReadPage(0, 10), Is.EqualTo(new[] { recent, pin }));
    }
    [Test]
    public void LegacyProjectHasUnpinnedUnknownMetadata()
    {
        var json = JObject.Parse(ProjectDataSerializer.Serialize(ProjectDataSerializer.Deserialize("{}")));
        Assert.That(json.Value<bool?>("IsPinned"), Is.False);
        Assert.That(json["LastChanged"], Is.Not.Null);
    }

    [TestCase("{\"IsPinned\":\"true\"}")]
    [TestCase("{\"LastChanged\":12}")]
    [TestCase("{\"IsPinned\":true,\"ispinned\":false}")]
    public void InvalidMetadataIsRejected(string json) =>
        Assert.Throws<InvalidDataException>(() => ProjectDataSerializer.Deserialize(json));

    [Test]
    public async Task ProjectsUseStoredTimeInsteadOfDirectoryTimeAndKeepCorruptSiblings()
    {
        var root = Directory.CreateTempSubdirectory("ohs-history-");
        try
        {
            var projects = root.CreateSubdirectory("projects");
            var old = projects.CreateSubdirectory("Old");
            var recent = projects.CreateSubdirectory("Recent");
            var broken = projects.CreateSubdirectory("Broken");
            await File.WriteAllTextAsync(Path.Combine(old.FullName, "project.json"), "{\"LastChanged\":\"2020-01-01T00:00:00Z\"}");
            await File.WriteAllTextAsync(Path.Combine(recent.FullName, "project.json"), "{\"LastChanged\":\"2026-10-18T01:00:00Z\"}");
            await File.WriteAllTextAsync(Path.Combine(broken.FullName, "project.json"), "broken");
            old.LastWriteTimeUtc = DateTime.UtcNow.AddDays(1);
            recent.LastWriteTimeUtc = DateTime.UnixEpoch;
            var snapshot = await new HomeFolderService(root.FullName).LoadProjectsAsync(default);
            Assert.That(snapshot.ReadPage(0, 10).Select(x => x.Name), Is.EqualTo(new[] { "Recent", "Old", "Broken" }));
            Assert.That(await File.ReadAllTextAsync(Path.Combine(broken.FullName, "project.json")), Is.EqualTo("broken"));
        }
        finally { root.Delete(true); }
    }
}
