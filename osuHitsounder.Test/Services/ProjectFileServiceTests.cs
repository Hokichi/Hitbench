using Newtonsoft.Json.Linq;
using osuHitsounder.Services.ProjectFiles;

namespace osuHitsounder.Test.Services;

public class ProjectFileServiceTests
{
    [Test]
    public void MetadataMutationsRejectProjectRootAndNestedPaths()
    {
        var service = new ProjectFileService(applicationDirectory);
        foreach (var path in new[] { Path.Combine(applicationDirectory, "projects", "project.json"),
                     Path.Combine(applicationDirectory, "projects", "one", "nested", "project.json") })
        {
            Assert.ThrowsAsync<ArgumentException>(() => service.SetPinnedAsync(path, true, default));
            Assert.ThrowsAsync<ArgumentException>(() => service.DeleteAsync(path, default));
        }
    }

    [Test]
    public async Task PinPreservesDateLikeExtensionStrings()
    {
        const string extension = "2026-10-18T08:00:00-11:00";
        var service = new ProjectFileService(applicationDirectory);
        var path = await service.CreateAsync(song, "Extension", default);
        var json = JObject.Parse(await File.ReadAllTextAsync(path));
        json["DateLikeExtension"] = extension;
        await File.WriteAllTextAsync(path, json.ToString());
        await service.SetPinnedAsync(path, true, default);
        var data = Newtonsoft.Json.JsonConvert.DeserializeObject<Dictionary<string, object>>(await File.ReadAllTextAsync(path),
            new Newtonsoft.Json.JsonSerializerSettings { DateParseHandling = Newtonsoft.Json.DateParseHandling.None });
        Assert.That(data!["DateLikeExtension"], Is.EqualTo(extension));
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task DeletionRejectsLinkedDescendantsIncludingDanglingLinks(bool dangling)
    {
        var service = new ProjectFileService(applicationDirectory);
        var path = await service.CreateAsync(song, "Linked", default);
        var outside = Directory.CreateDirectory(Path.Combine(root, "External"));
        await File.WriteAllTextAsync(Path.Combine(outside.FullName, "keep.txt"), "keep");
        var link = Path.Combine(Path.GetDirectoryName(path)!, "LinkedSamples");
        Directory.CreateSymbolicLink(link, outside.FullName);
        if (dangling) outside.Delete(true);
        Assert.ThrowsAsync<ArgumentException>(() => service.DeleteAsync(path, default));
        Assert.That(File.Exists(path), Is.True);
        if (!dangling) Assert.That(await File.ReadAllTextAsync(Path.Combine(outside.FullName, "keep.txt")), Is.EqualTo("keep"));
    }

    [Test]
    public async Task PinRejectsLinkedJsonAndLockedWritesPreserveOriginal()
    {
        var service = new ProjectFileService(applicationDirectory);
        var path = await service.CreateAsync(song, "Locked", default);
        var before = await File.ReadAllTextAsync(path);
        using (var locked = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            await Assert.ThatAsync(() => service.SetPinnedAsync(path, true, default),
                Throws.InstanceOf<IOException>().Or.InstanceOf<UnauthorizedAccessException>());
        Assert.That(await File.ReadAllTextAsync(path), Is.EqualTo(before));
        var outside = Path.Combine(root, "external.json");
        await File.WriteAllTextAsync(outside, before);
        File.Delete(path); File.CreateSymbolicLink(path, outside);
        Assert.ThrowsAsync<ArgumentException>(() => service.SetPinnedAsync(path, true, default));
        Assert.ThrowsAsync<ArgumentException>(() => service.DeleteAsync(path, default));
        Assert.That(await File.ReadAllTextAsync(outside), Is.EqualTo(before));
    }

    [Test]
    public async Task PinOnlyChangesPinnedMetadataAndDeletionPreservesOriginalMapset()
    {
        var service = new ProjectFileService(applicationDirectory);
        var path = await service.CreateAsync(song, "History", default);
        var before = JObject.Parse(await File.ReadAllTextAsync(path));
        before["Extension"] = new JObject { ["Keep"] = true };
        await File.WriteAllTextAsync(path, before.ToString());
        await service.SetPinnedAsync(path, true, default);
        var after = JObject.Parse(await File.ReadAllTextAsync(path));
        Assert.That(after.Value<bool>("IsPinned"), Is.True);
        after.Remove("IsPinned"); before.Remove("IsPinned");
        Assert.That(JToken.DeepEquals(before, after), Is.True);
        await service.SetPinnedAsync(path, false, default);
        Assert.That(JObject.Parse(await File.ReadAllTextAsync(path)).Value<bool>("IsPinned"), Is.False);
        Directory.CreateDirectory(Path.Combine(Path.GetDirectoryName(path)!, "Samples"));
        await service.DeleteAsync(path, default);
        Assert.That(Directory.Exists(Path.GetDirectoryName(path)), Is.False);
        Assert.That(song.Exists, Is.True);
    }

    [Test]
    public async Task MetadataMutationsRejectOutsideAndCancelledTargets()
    {
        var service = new ProjectFileService(applicationDirectory);
        var outside = Path.Combine(song.FullName, "project.json");
        await File.WriteAllTextAsync(outside, "{}");
        Assert.ThrowsAsync<ArgumentException>(() => service.SetPinnedAsync(outside, true, default));
        Assert.ThrowsAsync<ArgumentException>(() => service.DeleteAsync(outside, default));
        var path = await service.CreateAsync(song, "Cancelled", default);
        var before = await File.ReadAllTextAsync(path);
        Assert.CatchAsync<OperationCanceledException>(() => service.SetPinnedAsync(path, true, new CancellationToken(true)));
        Assert.That(await File.ReadAllTextAsync(path), Is.EqualTo(before));
    }

    private string root = null!;
    private string applicationDirectory = null!;
    private DirectoryInfo song = null!;

    [SetUp]
    public void SetUp()
    {
        root = Path.Combine(Path.GetTempPath(), "osuHitsounder-project-tests", Guid.NewGuid().ToString("N"));
        applicationDirectory = Path.Combine(root, "App");
        song = Directory.CreateDirectory(Path.Combine(root, "Songs", "Selected"));
        File.WriteAllText(Path.Combine(song.FullName, "a.osu"), "[TimingPoints]\n125,500,4,1,0,100,1,0\n");
    }

    [TearDown]
    public void TearDown() => Directory.Delete(root, true);

    [Test]
    public async Task CreatesProjectWithAbsoluteBeatmapPathAndTimingLists()
    {
        var path = await new ProjectFileService(applicationDirectory).CreateAsync(song, "Manual name", CancellationToken.None);
        var json = JObject.Parse(await File.ReadAllTextAsync(path));
        Assert.That(path, Is.EqualTo(Path.Combine(applicationDirectory, "projects", "Manual name", "project.json")));
        Assert.That((string?)json["BeatmapPath"], Is.EqualTo(song.FullName));
        Assert.That((JArray)json["Tracks"]!, Is.Empty);
        Assert.That((JArray)json["ControlPoints"]!, Is.Empty);
        Assert.That((JArray)json["Offsets"]!, Is.EqualTo(new JArray(125)));
        Assert.That((JArray)json["BPMs"]!, Is.EqualTo(new JArray(120)));
        Assert.That(Directory.GetFiles(Path.GetDirectoryName(path)!), Has.Length.EqualTo(1));
    }

    [Test]
    public void InvalidTimingDoesNotPublishProject()
    {
        File.WriteAllText(Path.Combine(song.FullName, "a.osu"), "[TimingPoints]\n0,0,4,1,0,100,1,0\n");
        Assert.ThrowsAsync<InvalidDataException>(() =>
            new ProjectFileService(applicationDirectory).CreateAsync(song, "Broken", CancellationToken.None));
        Assert.That(Directory.Exists(Path.Combine(applicationDirectory, "projects", "Broken")), Is.False);
    }

    [Test]
    public void MissingSongDoesNotCreateProject()
    {
        song.Delete(recursive: true);
        Assert.ThrowsAsync<DirectoryNotFoundException>(() =>
            new ProjectFileService(applicationDirectory).CreateAsync(song, "Manual name", CancellationToken.None));
        Assert.That(Directory.Exists(applicationDirectory), Is.False);
    }

    [Test]
    public async Task ExistingProjectIsPreserved()
    {
        var destination = Directory.CreateDirectory(Path.Combine(applicationDirectory, "projects", "Selected"));
        var path = Path.Combine(destination.FullName, "project.json");
        await File.WriteAllTextAsync(path, "existing project");
        Assert.ThrowsAsync<IOException>(() =>
            new ProjectFileService(applicationDirectory).CreateAsync(song, "Selected", CancellationToken.None));
        Assert.That(await File.ReadAllTextAsync(path), Is.EqualTo("existing project"));
        Assert.That(destination.GetFiles(), Has.Length.EqualTo(1));
    }

    [Test]
    public async Task CancelledSavePreservesExistingProjectJson()
    {
        var projectFolder = Directory.CreateDirectory(Path.Combine(applicationDirectory, "projects", "Existing"));
        var path = Path.Combine(projectFolder.FullName, "project.json");
        await File.WriteAllTextAsync(path, "old");
        Assert.CatchAsync<OperationCanceledException>(() =>
            new ProjectFileService(applicationDirectory).SaveAsync(path, "new", new CancellationToken(true)));
        Assert.That(await File.ReadAllTextAsync(path), Is.EqualTo("old"));
        Assert.That(Directory.GetFiles(projectFolder.FullName), Has.Length.EqualTo(1));
    }

    [Test]
    public void CancellationDoesNotCreateProject()
    {
        Assert.ThrowsAsync<OperationCanceledException>(() =>
            new ProjectFileService(applicationDirectory).CreateAsync(song, "Selected", new CancellationToken(true)));
        Assert.That(Directory.Exists(applicationDirectory), Is.False);
    }

    [Test]
    public async Task BlockedProjectDirectoryDoesNotChangeExistingFile()
    {
        var projects = Directory.CreateDirectory(Path.Combine(applicationDirectory, "projects"));
        var blocker = Path.Combine(projects.FullName, "Selected");
        await File.WriteAllTextAsync(blocker, "keep");
        Assert.ThrowsAsync<IOException>(() =>
            new ProjectFileService(applicationDirectory).CreateAsync(song, "Selected", CancellationToken.None));
        Assert.That(await File.ReadAllTextAsync(blocker), Is.EqualTo("keep"));
        Assert.That(projects.GetFiles(), Has.Length.EqualTo(1));
    }

    [Test]
    public void FailedPublishRemovesTemporaryFile()
    {
        var destination = Directory.CreateDirectory(Path.Combine(applicationDirectory, "projects", "Selected"));
        Directory.CreateDirectory(Path.Combine(destination.FullName, "project.json"));
        Assert.ThrowsAsync<IOException>(() =>
            new ProjectFileService(applicationDirectory).CreateAsync(song, "Selected", CancellationToken.None));
        Assert.That(destination.GetFiles(), Is.Empty);
        Assert.That(destination.GetDirectories(), Has.Length.EqualTo(1));
    }

    [TestCase("", "Enter a project name.")]
    [TestCase("   ", "Enter a project name.")]
    [TestCase("A:B", "Project name contains invalid characters.")]
    [TestCase("name.", "Project name cannot end with a dot or space.")]
    [TestCase("name ", "Project name cannot end with a dot or space.")]
    [TestCase("CON", "Project name is reserved by Windows.")]
    [TestCase("con.txt", "Project name is reserved by Windows.")]
    public void ProjectNameSyntaxErrorsAreSpecific(string input, string expected) =>
        Assert.That(ProjectNameRules.GetSyntaxError(input), Is.EqualTo(expected));

    [TestCase("Artist - Title")]
    [TestCase("アーティスト 曲")]
    public void ValidProjectNamesHaveNoSyntaxError(string input) =>
        Assert.That(ProjectNameRules.GetSyntaxError(input), Is.Null);

    [TestCase("A:B/C", "ABC")]
    [TestCase("Artist - Title", "Artist - Title")]
    public void InvalidCharactersAreRemoved(string input, string expected) =>
        Assert.That(ProjectNameRules.RemoveInvalidCharacters(input), Is.EqualTo(expected));

    [Test]
    public async Task ExistingNamesIncludeOnlyDirectProjectDirectoriesCaseInsensitively()
    {
        var projects = Directory.CreateDirectory(Path.Combine(applicationDirectory, "projects"));
        projects.CreateSubdirectory("Alpha");
        projects.CreateSubdirectory("beta").CreateSubdirectory("nested");
        await File.WriteAllTextAsync(Path.Combine(projects.FullName, "ignored.txt"), "file");

        var names = await new ProjectFileService(applicationDirectory).GetExistingNamesAsync(CancellationToken.None);

        Assert.That(names, Is.EquivalentTo(new[] { "Alpha", "beta" }));
        Assert.That(names.Contains("ALPHA"), Is.True);
    }

    [Test]
    public async Task EmptyExistingNamesDoesNotCreateProjectsRoot()
    {
        var names = await new ProjectFileService(applicationDirectory).GetExistingNamesAsync(CancellationToken.None);

        Assert.That(names, Is.Empty);
        Assert.That(Directory.Exists(applicationDirectory), Is.False);
    }

    [TestCase("A:B")]
    [TestCase("CON")]
    public void InvalidNameDoesNotCreateProject(string projectName)
    {
        Assert.ThrowsAsync<ArgumentException>(() =>
            new ProjectFileService(applicationDirectory).CreateAsync(song, projectName, CancellationToken.None));
        Assert.That(Directory.Exists(applicationDirectory), Is.False);
    }

    [Test]
    public async Task CreateRechecksCollisionAfterExistingNamesSnapshot()
    {
        var service = new ProjectFileService(applicationDirectory);
        Assert.That(await service.GetExistingNamesAsync(CancellationToken.None), Is.Empty);
        Directory.CreateDirectory(Path.Combine(applicationDirectory, "projects", "Later"));

        Assert.ThrowsAsync<IOException>(() => service.CreateAsync(song, "Later", CancellationToken.None));
    }
}
