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
        var path = await new ProjectFileService(applicationDirectory).CreateAsync(song, CancellationToken.None);
        var json = JObject.Parse(await File.ReadAllTextAsync(path));
        Assert.That(path, Is.EqualTo(Path.Combine(applicationDirectory, "projects", "Selected", "project.json")));
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
            new ProjectFileService(applicationDirectory).CreateAsync(song, CancellationToken.None));
        Assert.That(Directory.Exists(applicationDirectory), Is.False);
    }

    [Test]
    public async Task ExistingProjectIsPreserved()
    {
        var destination = Directory.CreateDirectory(Path.Combine(applicationDirectory, "projects", "Selected"));
        var path = Path.Combine(destination.FullName, "project.json");
        await File.WriteAllTextAsync(path, "existing project");
        Assert.ThrowsAsync<IOException>(() =>
            new ProjectFileService(applicationDirectory).CreateAsync(song, CancellationToken.None));
        Assert.That(await File.ReadAllTextAsync(path), Is.EqualTo("existing project"));
        Assert.That(destination.GetFiles(), Has.Length.EqualTo(1));
    }

    [Test]
    public void CancellationDoesNotCreateProject()
    {
        Assert.ThrowsAsync<OperationCanceledException>(() =>
            new ProjectFileService(applicationDirectory).CreateAsync(song, new CancellationToken(true)));
        Assert.That(Directory.Exists(applicationDirectory), Is.False);
    }

    [Test]
    public async Task BlockedProjectDirectoryDoesNotChangeExistingFile()
    {
        var projects = Directory.CreateDirectory(Path.Combine(applicationDirectory, "projects"));
        var blocker = Path.Combine(projects.FullName, "Selected");
        await File.WriteAllTextAsync(blocker, "keep");
        Assert.ThrowsAsync<IOException>(() =>
            new ProjectFileService(applicationDirectory).CreateAsync(song, CancellationToken.None));
        Assert.That(await File.ReadAllTextAsync(blocker), Is.EqualTo("keep"));
        Assert.That(projects.GetFiles(), Has.Length.EqualTo(1));
    }

    [Test]
    public void FailedPublishRemovesTemporaryFile()
    {
        var destination = Directory.CreateDirectory(Path.Combine(applicationDirectory, "projects", "Selected"));
        Directory.CreateDirectory(Path.Combine(destination.FullName, "project.json"));
        Assert.ThrowsAsync<IOException>(() =>
            new ProjectFileService(applicationDirectory).CreateAsync(song, CancellationToken.None));
        Assert.That(destination.GetFiles(), Is.Empty);
        Assert.That(destination.GetDirectories(), Has.Length.EqualTo(1));
    }
}
