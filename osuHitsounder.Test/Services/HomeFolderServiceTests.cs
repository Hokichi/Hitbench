using osuHitsounder.Services.FileSystem;

namespace osuHitsounder.Test.Services;

public class HomeFolderServiceTests
{
    private string root = null!;
    private string appRoot = null!;

    [SetUp]
    public void SetUp()
    {
        root = Directory.CreateTempSubdirectory("osuHitsounder-tests-").FullName;
        appRoot = Path.Combine(root, "app");
    }

    [TearDown]
    public void TearDown() => Directory.Delete(root, true);

    [Test]
    public async Task CreatesProjectsAndListsOnlyImmediateSongFolders()
    {
        var install = Directory.CreateDirectory(Path.Combine(root, "game"));
        File.WriteAllText(Path.Combine(install.FullName, "OSU!.EXE"), "");
        Directory.CreateDirectory(Path.Combine(install.FullName, "Songs", "A", "nested"));
        Directory.CreateDirectory(Path.Combine(install.FullName, "Songs", "B"));
        var hidden = Directory.CreateDirectory(Path.Combine(install.FullName, "Songs", "Hidden"));
        hidden.Attributes |= FileAttributes.Hidden;
        Directory.CreateDirectory(Path.Combine(appRoot, "projects", "Keep"));

        var result = await new HomeFolderService(appRoot, () => [root]).LoadAsync(CancellationToken.None);

        Assert.That(Directory.Exists(Path.Combine(appRoot, "projects")), Is.True);
        Assert.That(result.Projects.Select(x => x.Name), Is.EqualTo(new[] { "Keep" }));
        Assert.That(result.Songs.Select(x => x.Name), Is.EqualTo(new[] { "A", "B", "Hidden" }));
        Assert.That(result.OsuDirectory, Is.EqualTo(install.FullName));
    }

    [Test]
    public async Task MissingExecutableDoesNotMatchBackupOrDirectory()
    {
        Directory.CreateDirectory(Path.Combine(root, "osu!.exe"));
        File.WriteAllText(Path.Combine(root, "osu!.exe.bak"), "");
        var result = await new HomeFolderService(appRoot, () => [root]).LoadAsync(CancellationToken.None);
        Assert.That(result.OsuDirectory, Is.Null);
        Assert.That(Directory.Exists(Path.Combine(appRoot, "projects")), Is.True);
    }

    [Test]
    public async Task MissingSongsIsNotCreatedAndMissingRootDoesNotAbortSearch()
    {
        File.WriteAllText(Path.Combine(root, "osu!.exe"), "");
        var result = await new HomeFolderService(appRoot, () => [Path.Combine(root, "gone"), root])
            .LoadAsync(CancellationToken.None);
        Assert.That(result.OsuDirectory, Is.EqualTo(root));
        Assert.That(result.SongsFolderExists, Is.False);
        Assert.That(Directory.Exists(Path.Combine(root, "Songs")), Is.False);
    }

    [Test]
    public void CancelledLoadDoesNotCreateProjects()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.CatchAsync<OperationCanceledException>(async () =>
            await new HomeFolderService(appRoot, () => [root]).LoadAsync(cancellation.Token));
        Assert.That(Directory.Exists(appRoot), Is.False);
    }

    [Test]
    public void ProjectCreationFailureIsNotReportedAsMissingOsu()
    {
        Directory.CreateDirectory(appRoot);
        File.WriteAllText(Path.Combine(appRoot, "projects"), "keep");
        Assert.ThrowsAsync<IOException>(async () =>
            await new HomeFolderService(appRoot, () => [root]).LoadAsync(CancellationToken.None));
    }
}
