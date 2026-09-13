using osuHitsounder.Services.FileSystem;

namespace osuHitsounder.Test.Services;

public class HomeFolderServiceTests
{
    private string root = null!;
    private string appRoot = null!;
    [SetUp] public void SetUp()
    {
        root = Directory.CreateTempSubdirectory("osuHitsounder-tests-").FullName;
        appRoot = Path.Combine(root, "app");
    }
    [TearDown] public void TearDown() => Directory.Delete(root, true);

    [Test]
    public async Task CreatesProjectsAndListsOnlyImmediateSongFolders()
    {
        var install = Directory.CreateDirectory(Path.Combine(root, "game"));
        File.WriteAllText(Path.Combine(install.FullName, "OSU!.EXE"), "");
        var a = Directory.CreateDirectory(Path.Combine(install.FullName, "Songs", "A"));
        a.CreateSubdirectory("nested");
        var b = Directory.CreateDirectory(Path.Combine(install.FullName, "Songs", "B"));
        var hidden = Directory.CreateDirectory(Path.Combine(install.FullName, "Songs", "Hidden"));
        hidden.Attributes |= FileAttributes.Hidden;
        foreach (var folder in new[] { a, b, hidden })
        {
            File.WriteAllText(Path.Combine(folder.FullName, "map.osu"), "[Metadata]\nArtistUnicode:Artist\nTitleUnicode:Title\n");
            folder.LastWriteTimeUtc = DateTime.UnixEpoch.AddDays(1);
        }
        Directory.CreateDirectory(Path.Combine(appRoot, "projects", "Keep"));
        var service = new HomeFolderService(appRoot, (_, _) => [install.FullName]);
        var projects = await service.LoadProjectsAsync(CancellationToken.None);
        var scan = await service.FindInstallationAsync(CancellationToken.None);
        var songs = await service.LoadSongsAsync(scan.Directory!, CancellationToken.None);
        Assert.That(projects.ReadPage(0, 64).Select(x => x.Name), Is.EqualTo(new[] { "Keep" }));
        Assert.That(songs!.ReadPage(0, 64).Select(x => x.Name), Is.EqualTo(new[] { "A", "B", "Hidden" }));
        Assert.That(scan.Directory, Is.EqualTo(install.FullName));
    }

    [Test]
    public async Task MissingRootDoesNotAbortSearchAndSongsIsNotCreated()
    {
        File.WriteAllText(Path.Combine(root, "osu!.exe"), "");
        var service = new HomeFolderService(appRoot, (_, _) => [Path.Combine(root, "gone"), root]);
        var result = await service.FindInstallationAsync(CancellationToken.None);
        Assert.That(result.Directory, Is.EqualTo(root));
        Assert.That(await service.LoadSongsAsync(root, CancellationToken.None), Is.Null);
        Assert.That(Directory.Exists(Path.Combine(root, "Songs")), Is.False);
    }

    [Test]
    public async Task DoesNotSearchBelowRegisteredInstallationDirectory()
    {
        var nested = Directory.CreateDirectory(Path.Combine(root, "unregistered"));
        File.WriteAllText(Path.Combine(nested.FullName, "osu!.exe"), "");
        var service = new HomeFolderService(appRoot, (_, _) => [root]);
        Assert.That((await service.FindInstallationAsync(CancellationToken.None)).Directory, Is.Null);
    }

    [Test]
    public async Task SongsIncludeOnlyFoldersWithDirectBeatmaps()
    {
        var songs = Directory.CreateDirectory(Path.Combine(root, "Songs"));
        songs.CreateSubdirectory("empty");
        var nested = songs.CreateSubdirectory("nested-only").CreateSubdirectory("nested");
        File.WriteAllText(Path.Combine(nested.FullName, "nested.osu"), "[Metadata]\nArtistUnicode:Nested\nTitleUnicode:Nested\n");
        var valid = songs.CreateSubdirectory("valid");
        File.WriteAllText(Path.Combine(valid.FullName, "b.osu"), "[Metadata]\nArtistUnicode:Other\nTitleUnicode:Other\n");
        File.WriteAllText(Path.Combine(valid.FullName, "a.OSU"),
            "osu file format v14\r\n[General]\r\nTitleUnicode:Ignore\r\n[Metadata]\r\nArtistUnicode:作曲者\r\nTitleUnicode:曲:一\r\n[Difficulty]\r\n");
        var snapshot = await new HomeFolderService(appRoot).LoadSongsAsync(root, CancellationToken.None);
        Assert.That(snapshot!.ReadPage(0, 64).Select(row => row.Name), Is.EqualTo(new[] { "valid" }));
        var row = snapshot.ReadPage(0, 64).Single();
        Assert.That(row.DisplayName, Is.EqualTo("作曲者 - 曲:一"));
        Assert.That(row.Directory.FullName, Is.EqualTo(valid.FullName));
    }

    [Test]
    public void CancelledLoadDoesNotCreateProjects()
    {
        Assert.CatchAsync<OperationCanceledException>(() =>
            new HomeFolderService(appRoot).LoadProjectsAsync(new CancellationToken(true)));
        Assert.That(Directory.Exists(appRoot), Is.False);
    }

    [Test]
    public void ProjectCreationFailureIsNotReportedAsMissingOsu()
    {
        Directory.CreateDirectory(appRoot);
        File.WriteAllText(Path.Combine(appRoot, "projects"), "keep");
        Assert.ThrowsAsync<IOException>(() => new HomeFolderService(appRoot).LoadProjectsAsync(CancellationToken.None));
    }
}
