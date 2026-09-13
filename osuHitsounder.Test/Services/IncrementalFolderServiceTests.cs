using osuHitsounder.Services.FileSystem;

namespace osuHitsounder.Test.Services;

public class IncrementalFolderServiceTests
{
    [Test]
    public async Task SnapshotSortsImmediateHiddenFoldersByTheirOwnTimestamp()
    {
        var root = Directory.CreateTempSubdirectory("osu-snapshot-");
        try
        {
            var songs = root.CreateSubdirectory("Songs");
            var old = songs.CreateSubdirectory("Old");
            old.CreateSubdirectory("Nested");
            var newest = songs.CreateSubdirectory("Newest");
            newest.Attributes |= FileAttributes.Hidden;
            foreach (var folder in new[] { old, newest })
            {
                File.WriteAllText(Path.Combine(folder.FullName, "map.osu"), "[Metadata]\nArtistUnicode:Artist\nTitleUnicode:Title\n");
                File.WriteAllText(Path.Combine(folder.FullName, "background.jpg"), "image");
            }
            old.LastWriteTimeUtc = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            newest.LastWriteTimeUtc = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            var service = new HomeFolderService(root.FullName, (_, _) => []);
            var snapshot = await service.LoadSongsAsync(root.FullName, CancellationToken.None);
            Assert.That(snapshot!.ReadPage(0, 64).Select(x => x.Name), Is.EqualTo(new[] { "Newest", "Old" }));
            Assert.That(await service.LoadSongsAsync(old.FullName, CancellationToken.None), Is.Null);
        }
        finally { root.Delete(true); }
    }

    [TestCase("osu!.exe")]
    [TestCase("OSU!.EXE")]
    public async Task DiscoversAndValidatesStableExecutable(string name)
    {
        var root = Directory.CreateTempSubdirectory("osu-discovery-");
        try
        {
            await File.WriteAllTextAsync(Path.Combine(root.FullName, name), "");
            var service = new HomeFolderService(root.FullName, (_, _) => [root.FullName]);
            Assert.That(await service.IsInstallationAsync(root.FullName, CancellationToken.None), Is.True);
            Assert.That((await service.FindInstallationAsync(CancellationToken.None)).Directory, Is.EqualTo(root.FullName));
        }
        finally { root.Delete(true); }
    }

    [Test]
    public async Task RejectsLazerOnlyInstallation()
    {
        var root = Directory.CreateTempSubdirectory("osu-lazer-rejected-");
        try
        {
            File.WriteAllText(Path.Combine(root.FullName, "osu!lazer.exe"), "");
            var service = new HomeFolderService(root.FullName, (_, _) => [root.FullName]);
            Assert.That(await service.IsInstallationAsync(root.FullName, CancellationToken.None), Is.False);
            Assert.That((await service.FindInstallationAsync(CancellationToken.None)).Directory, Is.Null);
        }
        finally { root.Delete(true); }
    }

    [Test]
    public async Task DoesNotAcceptDirectoryOrBackupAsExecutable()
    {
        var root = Directory.CreateTempSubdirectory("osu-invalid-");
        try
        {
            root.CreateSubdirectory("osu!.exe");
            await File.WriteAllTextAsync(Path.Combine(root.FullName, "osu!lazer.exe.bak"), "");
            var service = new HomeFolderService(root.FullName, (_, _) => [root.FullName]);
            Assert.That(await service.IsInstallationAsync(root.FullName, CancellationToken.None), Is.False);
            Assert.That(await service.IsInstallationAsync("relative", CancellationToken.None), Is.False);
            var scan = await service.FindInstallationAsync(CancellationToken.None);
            Assert.That(scan.Directory, Is.Null);
        }
        finally { root.Delete(true); }
    }

    [Test]
    public async Task CancellationInterruptsCandidateEnumeration()
    {
        using var cancellation = new CancellationTokenSource();
        IEnumerable<string> Candidates(CancellationToken token, Action skipped)
        {
            yield return Path.Combine(Path.GetTempPath(), "missing-osu-installation");
            cancellation.Cancel();
            token.ThrowIfCancellationRequested();
            yield return Path.GetTempPath();
        }
        var service = new HomeFolderService(Path.GetTempPath(), Candidates);
        await Assert.ThatAsync(() => service.FindInstallationAsync(cancellation.Token),
            Throws.InstanceOf<OperationCanceledException>());
    }

    [Test]
    public void CancelledDiscoveryDoesNotEnumerateApplications()
    {
        var service = new HomeFolderService(Path.GetTempPath(), (_, _) => throw new AssertionException("Must not enumerate"));
        Assert.CatchAsync<OperationCanceledException>(() => service.FindInstallationAsync(new CancellationToken(true)));
    }
}

