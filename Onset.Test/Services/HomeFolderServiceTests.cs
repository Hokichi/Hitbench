using Onset.Services.FileSystem;

namespace Onset.Test.Services;

public class HomeFolderServiceTests
{
    [Test]
    public async Task BootCreatesSavedSamplesForValidProjectsAndPreservesFailures()
    {
        foreach (var name in new[] { "One", "Two", "Blocked", "Invalid" })
        {
            var folder = Directory.CreateDirectory(Path.Combine(appRoot, "projects", name));
            File.WriteAllText(Path.Combine(folder.FullName, "project.json"), name == "Invalid" ? "broken" : "{\"BeatmapPath\":\"\"}");
        }
        var blocked = Path.Combine(appRoot, "projects", "Blocked", "Saved Samples");
        File.WriteAllText(blocked, "keep");
        var service = new HomeFolderService(appRoot);
        var snapshot = await service.LoadProjectsAsync(default);
        Assert.That(snapshot.Count, Is.EqualTo(4));
        foreach (var name in new[] { "One", "Two" })
            Assert.That(Directory.Exists(Path.Combine(appRoot, "projects", name, "Saved Samples")), Is.True);
        Assert.That(Directory.Exists(Path.Combine(appRoot, "projects", "Invalid", "Saved Samples")), Is.False);
        Assert.That(File.ReadAllText(blocked), Is.EqualTo("keep"));
        var sentinel = Path.Combine(appRoot, "projects", "One", "Saved Samples", "keep.wav");
        File.WriteAllText(sentinel, "audio");
        await service.LoadProjectsAsync(default);
        Assert.That(File.ReadAllText(sentinel), Is.EqualTo("audio"));
    }
    private string root = null!;
    private string appRoot = null!;
    [SetUp] public void SetUp()
    {
        root = Directory.CreateTempSubdirectory("Onset-tests-").FullName;
        appRoot = Path.Combine(root, "app");
    }
    [TearDown] public void TearDown() => Directory.Delete(root, true);

    [Test]
    public async Task WarmCacheReusesMetadataAcrossServiceInstances()
    {
        var folder = Directory.CreateDirectory(Path.Combine(root, "Songs", "A"));
        var map = Path.Combine(folder.FullName, "map.osu");
        File.WriteAllText(map, "[Metadata]\nArtistUnicode:Artist\nTitleUnicode:Original\n");
        File.WriteAllText(Path.Combine(folder.FullName, "bg.jpg"), "image");
        await new HomeFolderService(appRoot).LoadSongsAsync(root, CancellationToken.None);
        File.WriteAllText(map, "[Metadata]\nArtistUnicode:Artist\nTitleUnicode:Changed\n");
        var warm = await new HomeFolderService(appRoot).LoadSongsAsync(root, CancellationToken.None);
        Assert.That(warm!.ReadPage(0, 10).Single().Title, Is.EqualTo("Original"));
        Assert.That(File.Exists(Path.Combine(appRoot, "data.json")), Is.True);
    }

    [Test]
    public async Task CacheTracksAdditionsDeletionsAndEqualCountReplacements()
    {
        CreateCachedSong("A");
        var service = new HomeFolderService(appRoot);
        await service.LoadSongsAsync(root, CancellationToken.None);
        CreateCachedSong("B");
        var added = await service.LoadSongsAsync(root, CancellationToken.None);
        Assert.That(added!.ReadPage(0, 10).Select(x => x.Name), Is.EquivalentTo(new[] { "A", "B" }));
        Directory.Delete(Path.Combine(root, "Songs", "A"), true);
        CreateCachedSong("C");
        var replaced = await service.LoadSongsAsync(root, CancellationToken.None);
        Assert.That(replaced!.ReadPage(0, 10).Select(x => x.Name), Is.EquivalentTo(new[] { "B", "C" }));
        Directory.Delete(Path.Combine(root, "Songs", "B"), true);
        var removed = await service.LoadSongsAsync(root, CancellationToken.None);
        Assert.That(removed!.ReadPage(0, 10).Single().Name, Is.EqualTo("C"));
    }

    [TestCase("broken")]
    [TestCase("{\"Version\":2,\"Folders\":[],\"Songs\":[]}")]
    [TestCase("null")]
    public async Task InvalidCacheRebuilds(string contents)
    {
        CreateCachedSong("A");
        Directory.CreateDirectory(appRoot);
        File.WriteAllText(Path.Combine(appRoot, "data.json"), contents);
        var result = await new HomeFolderService(appRoot).LoadSongsAsync(root, CancellationToken.None);
        Assert.That(result!.Count, Is.EqualTo(1));
        Assert.That(File.ReadAllText(Path.Combine(appRoot, "data.json")), Does.Contain("Artist"));
    }

    [Test]
    public async Task CacheDoesNotReuseDifferentSongsRoot()
    {
        CreateCachedSong("A");
        await new HomeFolderService(appRoot).LoadSongsAsync(root, CancellationToken.None);
        var other = Directory.CreateDirectory(Path.Combine(root, "Other", "Songs", "B"));
        File.WriteAllText(Path.Combine(other.FullName, "map.osu"), "[Metadata]\nArtistUnicode:Other\nTitleUnicode:B\n");
        File.WriteAllText(Path.Combine(other.FullName, "bg.jpg"), "image");
        var result = await new HomeFolderService(appRoot).LoadSongsAsync(Path.Combine(root, "Other"), CancellationToken.None);
        Assert.That(result!.ReadPage(0, 10).Single().Artist, Is.EqualTo("Other"));
    }

    [Test]
    public async Task PreviouslyEmptyFolderIsRetriedAfterBeatmapAdded()
    {
        var folder = Directory.CreateDirectory(Path.Combine(root, "Songs", "A"));
        await new HomeFolderService(appRoot).LoadSongsAsync(root, CancellationToken.None);
        File.WriteAllText(Path.Combine(folder.FullName, "map.osu"), "[Metadata]\nTitleUnicode:Recovered\n");
        var result = await new HomeFolderService(appRoot).LoadSongsAsync(root, CancellationToken.None);
        Assert.That(result!.ReadPage(0, 10).Single().Title, Is.EqualTo("Recovered"));
    }

    [Test]
    public async Task OldCacheSkippedSongRecoversAndPersists()
    {
        var folder = Directory.CreateDirectory(Path.Combine(root, "Songs", "A"));
        var map = Path.Combine(folder.FullName, "map.osu");
        File.WriteAllText(map, "[Metadata]\nTitleUnicode:Recovered\n");
        Directory.CreateDirectory(appRoot);
        var cachePath = Path.Combine(appRoot, "data.json");
        File.WriteAllText(cachePath, System.Text.Json.JsonSerializer.Serialize(new
        {
            Version = 1, SongsRoot = Path.Combine(root, "Songs"), Folders = new[] { "A" }, Songs = Array.Empty<object>()
        }));
        var result = await new HomeFolderService(appRoot).LoadSongsAsync(root, CancellationToken.None);
        Assert.That(result!.ReadPage(0, 10).Single().Title, Is.EqualTo("Recovered"));
        using var cache = System.Text.Json.JsonDocument.Parse(File.ReadAllText(cachePath));
        Assert.That(cache.RootElement.GetProperty("Songs").GetArrayLength(), Is.EqualTo(1));
        File.WriteAllText(map, "[Metadata]\nTitleUnicode:Changed\n");
        var warm = await new HomeFolderService(appRoot).LoadSongsAsync(root, CancellationToken.None);
        Assert.That(warm!.ReadPage(0, 10).Single().Title, Is.EqualTo("Recovered"));
    }

    [Test]
    public async Task SkippedFoldersRemainAbsentUntilDirectBeatmapExists()
    {
        Directory.CreateDirectory(Path.Combine(root, "Songs", "empty"));
        var nested = Directory.CreateDirectory(Path.Combine(root, "Songs", "nested-only", "nested"));
        File.WriteAllText(Path.Combine(nested.FullName, "map.osu"), "[Metadata]\nTitleUnicode:Nested\n");
        var service = new HomeFolderService(appRoot);
        Assert.That((await service.LoadSongsAsync(root, CancellationToken.None))!.Count, Is.Zero);
        Assert.That((await service.LoadSongsAsync(root, CancellationToken.None))!.Count, Is.Zero);
    }

    [Test]
    public async Task CacheWriteFailureStillReturnsSongsAndCleansTemporaryFile()
    {
        CreateCachedSong("A");
        Directory.CreateDirectory(Path.Combine(appRoot, "data.json"));
        var result = await new HomeFolderService(appRoot).LoadSongsAsync(root, CancellationToken.None);
        Assert.That(result!.Count, Is.EqualTo(1));
        Assert.That(Directory.GetFiles(appRoot, "*.tmp"), Is.Empty);
    }

    [Test]
    public async Task CancelledLoadPreservesExistingCache()
    {
        CreateCachedSong("A");
        var service = new HomeFolderService(appRoot);
        await service.LoadSongsAsync(root, CancellationToken.None);
        var before = File.ReadAllBytes(Path.Combine(appRoot, "data.json"));
        Assert.ThrowsAsync<TaskCanceledException>(async () =>
            await service.LoadSongsAsync(root, new CancellationToken(true)));
        Assert.That(File.ReadAllBytes(Path.Combine(appRoot, "data.json")), Is.EqualTo(before));
    }

    private void CreateCachedSong(string name)
    {
        var folder = Directory.CreateDirectory(Path.Combine(root, "Songs", name));
        File.WriteAllText(Path.Combine(folder.FullName, "map.osu"), "[Metadata]\nArtistUnicode:Artist\nTitleUnicode:" + name + "\n");
        File.WriteAllText(Path.Combine(folder.FullName, "bg.jpg"), "image");
    }

    [Test]
    public async Task ForcedReloadRefreshesExistingMetadataAndBackground()
    {
        CreateCachedSong("A");
        var service = new HomeFolderService(appRoot);
        await service.LoadSongsAsync(root, CancellationToken.None);
        var folder = Path.Combine(root, "Songs", "A");
        File.WriteAllText(Path.Combine(folder, "map.osu"),
            "[Metadata]\nArtistUnicode:Edited\nTitleUnicode:Changed\n[Events]\n//Background and Video events\n0,0,\"new.jpg\",0,0\n");
        File.WriteAllText(Path.Combine(folder, "new.jpg"), "new image");
        var refreshed = await service.ReloadSongsAsync(root, CancellationToken.None);
        Assert.That(refreshed!.ReadPage(0, 1).Single().Title, Is.EqualTo("Changed"));
        Assert.That(refreshed.ReadPage(0, 1).Single().BackgroundPath, Is.EqualTo(Path.Combine(folder, "new.jpg")));
        var warm = await new HomeFolderService(appRoot).LoadSongsAsync(root, CancellationToken.None);
        Assert.That(warm!.ReadPage(0, 1).Single().Artist, Is.EqualTo("Edited"));
    }

    [Test]
    public async Task ForcedReloadFindsPreviouslySkippedFolders()
    {
        Directory.CreateDirectory(Path.Combine(root, "Songs", "A"));
        var service = new HomeFolderService(appRoot);
        await service.LoadSongsAsync(root, CancellationToken.None);
        CreateCachedSong("A");
        var result = await service.ReloadSongsAsync(root, CancellationToken.None);
        Assert.That(result!.ReadPage(0, 10).Single().Name, Is.EqualTo("A"));
    }

    [Test]
    public async Task CancelledForcedReloadPreservesCache()
    {
        CreateCachedSong("A");
        var service = new HomeFolderService(appRoot);
        await service.LoadSongsAsync(root, CancellationToken.None);
        var before = File.ReadAllBytes(Path.Combine(appRoot, "data.json"));
        Assert.ThrowsAsync<TaskCanceledException>(async () =>
            await service.ReloadSongsAsync(root, new CancellationToken(true)));
        Assert.That(File.ReadAllBytes(Path.Combine(appRoot, "data.json")), Is.EqualTo(before));
    }

    [Test]
    public async Task ForcedReloadTracksInventoryAndHandlesMissingRootAndCacheWriteFailure()
    {
        CreateCachedSong("A");
        var service = new HomeFolderService(appRoot);
        await service.LoadSongsAsync(root, CancellationToken.None);
        Directory.Delete(Path.Combine(root, "Songs", "A"), true);
        CreateCachedSong("B");
        File.Delete(Path.Combine(appRoot, "data.json"));
        Directory.CreateDirectory(Path.Combine(appRoot, "data.json"));
        var result = await service.ReloadSongsAsync(root, CancellationToken.None);
        Assert.That(result!.ReadPage(0, 10).Single().Name, Is.EqualTo("B"));
        Assert.That(Directory.GetFiles(appRoot, "*.tmp"), Is.Empty);
        Directory.Delete(Path.Combine(root, "Songs"), true);
        Assert.That(await service.ReloadSongsAsync(root, CancellationToken.None), Is.Null);
    }

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
            File.WriteAllText(Path.Combine(folder.FullName, "background.jpg"), "image");
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
        File.WriteAllText(Path.Combine(valid.FullName, "background.jpg"), "image");
        var snapshot = await new HomeFolderService(appRoot).LoadSongsAsync(root, CancellationToken.None);
        Assert.That(snapshot!.ReadPage(0, 64).Select(row => row.Name), Is.EqualTo(new[] { "valid" }));
        var row = snapshot.ReadPage(0, 64).Single();
        Assert.That(row.DisplayName, Is.EqualTo("作曲者 - 曲:一"));
        Assert.That(row.Directory.FullName, Is.EqualTo(valid.FullName));
    }

    [Test]
    public async Task SongUsesMetadataAndFirstBackgroundEventFromOrdinalFirstBeatmap()
    {
        var song = Directory.CreateDirectory(Path.Combine(root, "Songs", "song"));
        var background = Path.Combine(song.FullName, "cover,one.jpg");
        File.WriteAllText(background, "image");
        File.WriteAllText(Path.Combine(song.FullName, "z.osu"),
            "[Metadata]\nArtistUnicode:Wrong artist\nTitleUnicode:Wrong title\n");
        File.WriteAllText(Path.Combine(song.FullName, "a.osu"),
            "[Events]\n//Background and Video events\n0,0,\"cover,one.jpg\",0,0\n" +
            "[Metadata]\nArtistUnicode:Artist\nTitleUnicode:Title\n");

        var snapshot = await new HomeFolderService(appRoot).LoadSongsAsync(root, CancellationToken.None);

        var row = snapshot!.ReadPage(0, 64).Single();
        Assert.Multiple(() =>
        {
            Assert.That(row.Artist, Is.EqualTo("Artist"));
            Assert.That(row.Title, Is.EqualTo("Title"));
            Assert.That(row.DisplayName, Is.EqualTo("Artist - Title"));
            Assert.That(row.BackgroundPath, Is.EqualTo(background));
        });
    }

    [Test]
    public async Task SongUsesOrdinalFirstDirectImageWhenBeatmapHasNoBackgroundEvent()
    {
        var song = Directory.CreateDirectory(Path.Combine(root, "Songs", "song"));
        File.WriteAllText(Path.Combine(song.FullName, "map.osu"),
            "[Events]\n//Background and Video events\n[Metadata]\nArtistUnicode:Artist\nTitleUnicode:Title\n");
        var expected = Path.Combine(song.FullName, "A.png");
        File.WriteAllText(expected, "image");
        File.WriteAllText(Path.Combine(song.FullName, "z.jpg"), "image");
        var nested = song.CreateSubdirectory("nested");
        File.WriteAllText(Path.Combine(nested.FullName, "0.png"), "image");

        var snapshot = await new HomeFolderService(appRoot).LoadSongsAsync(root, CancellationToken.None);

        Assert.That(snapshot!.ReadPage(0, 64).Single().BackgroundPath, Is.EqualTo(expected));
    }

    [Test]
    public async Task SongWithoutArtworkDoesNotExcludeOtherFolders()
    {
        var songs = Directory.CreateDirectory(Path.Combine(root, "Songs"));
        var broken = songs.CreateSubdirectory("broken");
        File.WriteAllText(Path.Combine(broken.FullName, "map.osu"),
            "[Metadata]\nArtistUnicode:Broken\nTitleUnicode:Broken\n");
        var valid = songs.CreateSubdirectory("valid");
        File.WriteAllText(Path.Combine(valid.FullName, "map.osu"),
            "[Metadata]\nArtistUnicode:Artist\nTitleUnicode:Title\n");
        File.WriteAllText(Path.Combine(valid.FullName, "background.jpg"), "image");

        var snapshot = await new HomeFolderService(appRoot).LoadSongsAsync(root, CancellationToken.None);

        Assert.That(snapshot!.ReadPage(0, 64).Select(row => row.Name), Is.EquivalentTo(new[] { "broken", "valid" }));
    }

    [Test]
    public async Task DirectBeatmapWithoutArtworkIsIncluded()
    {
        var folder = Directory.CreateDirectory(Path.Combine(root, "Songs", "song"));
        File.WriteAllText(Path.Combine(folder.FullName, "map.OSU"),
            "[Metadata]\nArtistUnicode:Artist\nTitleUnicode:Title\n");
        var snapshot = await new HomeFolderService(appRoot).LoadSongsAsync(root, CancellationToken.None);
        var row = snapshot!.ReadPage(0, 10).Single();
        Assert.That(row.BackgroundPath, Is.Empty);
        Assert.That(row.DisplayName, Is.EqualTo("Artist - Title"));
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task UnsupportedOrNestedArtworkDoesNotExcludeSong(bool nested)
    {
        var folder = Directory.CreateDirectory(Path.Combine(root, "Songs", "song"));
        File.WriteAllText(Path.Combine(folder.FullName, "map.osu"), "[Metadata]\nTitleUnicode:Title\n");
        var imageFolder = nested ? folder.CreateSubdirectory("nested") : folder;
        File.WriteAllText(Path.Combine(imageFolder.FullName, nested ? "background.png" : "background.webp"), "image");
        var snapshot = await new HomeFolderService(appRoot).LoadSongsAsync(root, CancellationToken.None);
        Assert.That(snapshot!.ReadPage(0, 10).Single().BackgroundPath, Is.Empty);
    }

    [Test]
    public async Task MissingMetadataUsesFolderName()
    {
        var folder = Directory.CreateDirectory(Path.Combine(root, "Songs", "Untitled song"));
        File.WriteAllText(Path.Combine(folder.FullName, "map.osu"), "osu file format v14\n");
        var snapshot = await new HomeFolderService(appRoot).LoadSongsAsync(root, CancellationToken.None);
        var row = snapshot!.ReadPage(0, 10).Single();
        Assert.That(row.Title, Is.EqualTo(folder.Name));
        Assert.That(row.DisplayName, Is.EqualTo(folder.Name));
    }

    [Test]
    public async Task LockedBeatmapDoesNotExcludeDiscoveredSong()
    {
        var folder = Directory.CreateDirectory(Path.Combine(root, "Songs", "Locked song"));
        var map = Path.Combine(folder.FullName, "map.osu");
        File.WriteAllText(map, "[Metadata]\nTitleUnicode:Title\n");
        using var locked = File.Open(map, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        var snapshot = await new HomeFolderService(appRoot).LoadSongsAsync(root, CancellationToken.None);
        var row = snapshot!.ReadPage(0, 10).Single();
        Assert.That(row.Title, Is.EqualTo(folder.Name));
        Assert.That(row.BackgroundPath, Is.Empty);
    }

    [Test]
    public void CancelledSongsLoadStillPropagates()
    {
        Directory.CreateDirectory(Path.Combine(root, "Songs"));

        Assert.CatchAsync<OperationCanceledException>(() =>
            new HomeFolderService(appRoot).LoadSongsAsync(root, new CancellationToken(true)));
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
