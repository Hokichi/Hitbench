using System.Diagnostics;
using CommunityToolkit.Mvvm.Messaging;
using Soundie.Services.FileSystem;
using Soundie.Services.ProjectFiles;
using Soundie.Test.Presentation;
using Soundie.ViewModels.Shell.HomePage;

namespace Soundie.Test.Services;

public class HomeFolderPerformanceTests
{
    [TestCase(1000)]
    [TestCase(10000)]
    [Explicit("Local filesystem timing; not a CI latency assertion")]
    public async Task MeasureConfiguredInstallation(int count)
    {
        var root = Directory.CreateTempSubdirectory("osu-performance-");
        try
        {
            await File.WriteAllTextAsync(Path.Combine(root.FullName, "osu!.exe"), "");
            var songs = root.CreateSubdirectory("Songs");
            for (var i = 0; i < count; i++)
            {
                var folder = songs.CreateSubdirectory($"{i:D5}");
                await File.WriteAllTextAsync(Path.Combine(folder.FullName, "map.osu"),
                    $"[Metadata]\nArtistUnicode:Artist\nTitleUnicode:Title {i}\n");
                await File.WriteAllTextAsync(Path.Combine(folder.FullName, "background.jpg"), "image");
                folder.LastWriteTimeUtc = DateTime.UnixEpoch.AddMinutes(i);
            }
            var folders = new HomeFolderService(root.FullName,
                (_, _) => throw new AssertionException("Configured startup must not scan drives"));
            using var settings = new AppSettingsService(root.FullName);
            await settings.SaveOsuPathAsync(root.FullName, CancellationToken.None);
            for (var run = 0; run < 5; run++)
            {
                var timer = Stopwatch.StartNew();
                var baseline = songs.EnumerateDirectories().OrderBy(folder => folder.Name,
                    StringComparer.CurrentCultureIgnoreCase).ThenBy(folder => folder.FullName, StringComparer.Ordinal).ToArray();
                var baselineMs = timer.Elapsed.TotalMilliseconds;
                timer.Restart();
                var snapshot = await folders.LoadSongsAsync(root.FullName, CancellationToken.None);
                var metadataMs = timer.Elapsed.TotalMilliseconds;
                timer.Restart();
                var page = snapshot!.ReadPage(0, 64);
                var pageMs = timer.Elapsed.TotalMilliseconds;
                Assert.That(baseline, Has.Length.EqualTo(count));
                Assert.That(page, Has.Count.EqualTo(64));
                Assert.That(page[0].Name, Is.EqualTo($"{count - 1:D5}"));
                var vm = new HomePageVM(folders, settings, new StubInstallationDialogService
                    { Locate = _ => throw new AssertionException("Configured startup must not open picker") },
                    new StubProjectCreationDialogService(), new ImmediateUiScheduler(),
                    new ProjectFileService(root.FullName), new WeakReferenceMessenger());
                timer.Restart();
                await vm.LoadAsync(CancellationToken.None);
                var readyMs = timer.Elapsed.TotalMilliseconds;
                Assert.That(vm.FatalError, Is.Null);
                Assert.That(vm.Songs, Is.Empty);
                Assert.That(vm.HasMoreSongs, Is.True);
                await vm.LoadInitialSongsAsync(CancellationToken.None);
                vm.CompleteSongsPresentation(vm.SongsPresentationGeneration);
                Assert.That(vm.Songs, Has.Count.EqualTo(HomePageVM.InitialSongCount));
                Assert.That(vm.CreateProjectCommand.CanExecute(vm.Songs[0]), Is.True);
                vm.Deactivate();
                TestContext.Progress.WriteLine(
                    $"Folders={count} Run={run + 1} EagerListMs={baselineMs:F2} MetadataMs={metadataMs:F2} PageMs={pageMs:F2} ViewModelReadyMs={readyMs:F2}");
            }
        }
        finally { root.Delete(true); }
    }
}

