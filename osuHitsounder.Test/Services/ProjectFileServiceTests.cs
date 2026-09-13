using Newtonsoft.Json.Linq;
using osuHitsounder.Services.ProjectFiles;

namespace osuHitsounder.Test.Services;

public class ProjectFileServiceTests
{
    private string root = null!;
    private string applicationDirectory = null!;
    private DirectoryInfo song = null!;

    [SetUp]
    public void SetUp()
    {
        root = Path.Combine(Path.GetTempPath(), "osuHitsounder-project-tests", Guid.NewGuid().ToString("N"));
        applicationDirectory = Path.Combine(root, "App");
        song = Directory.CreateDirectory(Path.Combine(root, "Songs", "Selected"));
    }

    [TearDown]
    public void TearDown() => Directory.Delete(root, true);

    [Test]
    public async Task CreatesProjectWithAbsoluteBeatmapPathAndEmptyLists()
    {
        var path = await new ProjectFileService(applicationDirectory).CreateAsync(song, "Manual name", CancellationToken.None);
        var json = JObject.Parse(await File.ReadAllTextAsync(path));
        Assert.That(path, Is.EqualTo(Path.Combine(applicationDirectory, "projects", "Manual name", "project.json")));
        Assert.That((string?)json["BeatmapPath"], Is.EqualTo(song.FullName));
        Assert.That((JArray)json["Samples"]!, Is.Empty);
        Assert.That((JArray)json["ControlPoints"]!, Is.Empty);
        Assert.That(Directory.GetFiles(Path.GetDirectoryName(path)!), Has.Length.EqualTo(1));
    }

    [Test]
    public void MissingSongDoesNotCreateProject()
    {
        song.Delete();
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
