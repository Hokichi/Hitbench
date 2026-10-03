using CommunityToolkit.Mvvm.Messaging;
using osuHitsounder.Resources.DataModels;
using osuHitsounder.ViewModels.Shell.HomePage;

namespace osuHitsounder.Test.Presentation;

public class HomePageVMTests
{
    private static async Task PresentAsync(HomePageVM vm)
    {
        await vm.LoadInitialSongsAsync(CancellationToken.None);
        vm.CompleteSongsPresentation(vm.SongsPresentationGeneration);
    }

    [Test]
    public async Task CancellingSearchRestoresStateAndAllowsRetry()
    {
        var vm = CreateVM(new StubHomeFolderService
        {
            Songs = (_, _) => Task.FromResult<FolderSnapshot?>(new FolderSnapshot([
                new FolderEntry("one", "one", DateTimeOffset.MinValue)]))
        });
        await vm.LoadAsync(CancellationToken.None);
        await PresentAsync(vm);
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(vm.IsSearchingSongs) && vm.IsSearchingSongs)
                vm.SearchSongsCommand.Cancel();
        };
        vm.SongSearchText = "different";
        await vm.SearchSongsCommand.ExecutionTask!;
        Assert.That(vm.IsSearchingSongs, Is.False);
        Assert.That(vm.SongsStatus, Is.EqualTo("Song search cancelled."));
        Assert.That(vm.CanRetrySongs, Is.True);
        Assert.That(vm.ReloadSongsCommand.CanExecute(null), Is.True);
        vm.Deactivate();
    }

    [Test]
    public async Task SearchCoversUnpublishedInventoryAndRestoresBlankQuery()
    {
        var vm = CreateVM(new StubHomeFolderService
        {
            Songs = (_, _) => Task.FromResult<FolderSnapshot?>(new FolderSnapshot(Enumerable.Range(0, 100)
                .Select(i => new FolderEntry(i.ToString(), i == 0 ? "hidden MATCH" : "other", DateTimeOffset.UnixEpoch.AddMinutes(i)))))
        });
        await vm.LoadAsync(CancellationToken.None);
        await PresentAsync(vm);
        vm.SongSearchText = " match ";
        await vm.SearchSongsCommand.ExecutionTask!;
        await PresentAsync(vm);
        Assert.That(vm.Songs.Single().FullName, Is.EqualTo("0"));
        Assert.That(vm.HasMoreSongs, Is.False);
        vm.SongSearchText = "missing";
        await vm.SearchSongsCommand.ExecutionTask!;
        await PresentAsync(vm);
        Assert.That(vm.Songs, Is.Empty);
        Assert.That(vm.SongsStatus, Is.EqualTo("No songs match your search."));
        Assert.That(vm.CanRetrySongs, Is.False);
        vm.SongSearchText = " ";
        await vm.SearchSongsCommand.ExecutionTask!;
        await PresentAsync(vm);
        Assert.That(vm.Songs, Has.Count.EqualTo(45));
        Assert.That(vm.HasMoreSongs, Is.True);
        vm.Deactivate();
    }

    [Test]
    public async Task ReloadPreservesLatestQueryAndExpansionWithoutReloadingProjects()
    {
        var pending = new TaskCompletionSource<FolderSnapshot?>();
        var vm = CreateVM(new StubHomeFolderService
        {
            Songs = (_, _) => Task.FromResult<FolderSnapshot?>(new FolderSnapshot([new FolderEntry("old", "old", DateTimeOffset.MinValue)])),
            ReloadSongs = (_, _) => pending.Task,
            Projects = _ => Task.FromResult(new FolderSnapshot([new FolderEntry("project", "project", DateTimeOffset.MinValue)]))
        });
        await vm.LoadAsync(CancellationToken.None);
        await PresentAsync(vm);
        var project = vm.Projects.Single();
        var inventory = vm.SongsInventoryGeneration;
        vm.OpenSongSearchCommand.Execute(null);
        vm.SongSearchText = "old";
        await vm.SearchSongsCommand.ExecutionTask!;
        await PresentAsync(vm);
        var reload = vm.ReloadSongsCommand.ExecuteAsync(null);
        Assert.That(vm.ReloadSongsCommand.CanExecute(null), Is.False);
        vm.SongSearchText = "new";
        pending.SetResult(new FolderSnapshot([new FolderEntry("new", "NEW middle", DateTimeOffset.UtcNow), project]));
        await reload;
        await PresentAsync(vm);
        Assert.That(vm.Songs.Single().FullName, Is.EqualTo("new"));
        Assert.That(vm.SongsInventoryGeneration, Is.GreaterThan(inventory));
        Assert.That(vm.IsSongSearchExpanded, Is.True);
        Assert.That(vm.SongSearchText, Is.EqualTo("new"));
        Assert.That(vm.Projects.Single(), Is.SameAs(project));
        vm.CloseSongSearchCommand.Execute(null);
        await vm.SearchSongsCommand.ExecutionTask!;
        await PresentAsync(vm);
        Assert.That(vm.SongSearchText, Is.Empty);
        Assert.That(vm.IsSongSearchExpanded, Is.False);
        Assert.That(vm.Songs, Has.Count.EqualTo(2));
        vm.Deactivate();
    }

    [Test]
    public async Task SearchSupersedesPendingPagingAndOlderQuery()
    {
        var vm = CreateVM(new StubHomeFolderService
        {
            Songs = (_, _) => Task.FromResult<FolderSnapshot?>(new FolderSnapshot(Enumerable.Range(0, 200)
                .Select(i => new FolderEntry(i.ToString(), i % 2 == 0 ? "even" : "odd", DateTimeOffset.UnixEpoch.AddMinutes(i)))))
        });
        await vm.LoadAsync(CancellationToken.None);
        await PresentAsync(vm);
        var page = vm.LoadMoreSongsCommand.ExecuteAsync(100);
        vm.SongSearchText = "even";
        var older = vm.SearchSongsCommand.ExecutionTask!;
        vm.SongSearchText = "odd";
        await Task.WhenAll(page, older, vm.SearchSongsCommand.ExecutionTask!);
        await PresentAsync(vm);
        await vm.LoadMoreSongsCommand.ExecuteAsync(60);
        Assert.That(vm.Songs, Has.Count.EqualTo(100));
        Assert.That(vm.Songs.All(x => x.Name == "odd"), Is.True);
        Assert.That(vm.HasMoreSongs, Is.False);
        vm.Deactivate();
    }

    [Test]
    public async Task ReloadFailureCanRetryAndDeactivationRejectsLateResults()
    {
        var folders = new StubHomeFolderService
        {
            Songs = (_, _) => Task.FromResult<FolderSnapshot?>(new FolderSnapshot([new FolderEntry("old", "old", DateTimeOffset.MinValue)])),
            ReloadSongs = (_, _) => throw new IOException("Unavailable")
        };
        var vm = CreateVM(folders);
        await vm.LoadAsync(CancellationToken.None);
        await PresentAsync(vm);
        await vm.ReloadSongsCommand.ExecuteAsync(null);
        Assert.That(vm.IsProcessing, Is.False);
        Assert.That(vm.CanRetrySongs, Is.True);
        Assert.That(vm.SongsStatus, Does.Contain("Unavailable"));
        var pending = new TaskCompletionSource<FolderSnapshot?>();
        folders.ReloadSongs = (_, _) => pending.Task;
        var retry = vm.RetrySongsCommand.ExecuteAsync(null);
        vm.Deactivate();
        pending.SetResult(new FolderSnapshot([new FolderEntry("late", "late", DateTimeOffset.UtcNow)]));
        await retry;
        Assert.That(vm.Songs.Any(x => x.Name == "late"), Is.False);
        Assert.That(vm.CanInteract, Is.False);
        Assert.That(vm.IsProcessing, Is.False);
    }
    [Test]
    public async Task ProcessingWaitsForCurrentViewportPresentation()
    {
        var vm = CreateVM(new StubHomeFolderService
        {
            Songs = (_, _) => Task.FromResult<FolderSnapshot?>(new FolderSnapshot(
                [new FolderEntry(@"C:\Songs\One", "One", DateTimeOffset.MinValue)]))
        });
        await vm.LoadAsync(CancellationToken.None);
        Assert.That(vm.IsProcessing, Is.True);
        var generation = vm.SongsPresentationGeneration;
        await vm.LoadInitialSongsAsync(CancellationToken.None);
        Assert.That(vm.Songs.Count, Is.EqualTo(1));
        Assert.That(vm.CanInteract, Is.False);
        vm.CompleteSongsPresentation(generation - 1);
        Assert.That(vm.IsProcessing, Is.True);
        vm.CompleteSongsPresentation(generation);
        Assert.That(vm.IsProcessing, Is.False);
        Assert.That(vm.CanInteract, Is.True);
    }
    private static HomePageVM CreateVM(StubHomeFolderService folders) =>
        new(folders, new StubAppSettingsService(), new StubInstallationDialogService(),
            new StubProjectCreationDialogService(), new ImmediateUiScheduler(),
            new StubProjectFileService((_, _, _) => Task.FromResult("saved.json")), new WeakReferenceMessenger());

    [TestCase(true, "No song folders found.")]
    [TestCase(false, "Songs folder wasn't found.")]
    public async Task EmptySongsDistinguishesMissingFolder(bool exists, string status)
    {
        var vm = CreateVM(new StubHomeFolderService
        {
            Songs = (_, _) => Task.FromResult<FolderSnapshot?>(exists ? new FolderSnapshot([]) : null)
        });
        await vm.LoadAsync(CancellationToken.None);
        Assert.That(vm.FatalError, Is.Null);
        Assert.That(vm.ProjectsStatus, Is.Not.Empty);
        Assert.That(vm.SongsStatus, Is.EqualTo(status));
        Assert.That(vm.CanRetrySongs, Is.True);
    }

    [Test]
    public async Task ProjectsPublishWhileSongsArePending()
    {
        var pending = new TaskCompletionSource<FolderSnapshot?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var published = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var vm = CreateVM(new StubHomeFolderService
        {
            Projects = _ => Task.FromResult(new FolderSnapshot(
                new[] { new FolderEntry(@"C:\project", "project", DateTimeOffset.UtcNow) })),
            Songs = (_, token) => pending.Task.WaitAsync(token)
        });
        vm.Projects.CollectionChanged += (_, _) =>
        {
            if (vm.Projects.Count > 0) published.TrySetResult();
        };
        var load = vm.LoadAsync(CancellationToken.None);
        await published.Task.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.That(vm.Projects.Single().Name, Is.EqualTo("project"));
        Assert.That(load.IsCompleted, Is.False);
        pending.SetResult(new FolderSnapshot([]));
        await load;
        Assert.That(vm.ProjectsStatus, Is.Empty);
    }

    [Test]
    public async Task SongSnapshotWaitsForFixedInitialDemandThenUsesViewportSizedPages()
    {
        var pending = new TaskCompletionSource<FolderSnapshot?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var vm = CreateVM(new StubHomeFolderService { Songs = (_, token) => pending.Task.WaitAsync(token) });
        var load = vm.LoadAsync(CancellationToken.None);
        try
        {
            Assert.That(vm.IsProcessing, Is.True);
            Assert.That(vm.CanInteract, Is.False);
        }
        finally
        {
            pending.TrySetResult(new FolderSnapshot(Enumerable.Range(0, 130).Select(i =>
                new FolderEntry($@"C:\Songs\{i:D3}", $"{i:D3}", DateTimeOffset.UnixEpoch.AddMinutes(i)))));
            await load;
        }
        Assert.That(vm.Songs, Is.Empty);
        Assert.That(vm.HasMoreSongs, Is.True);
        Assert.That(vm.LoadMoreSongsCommand.CanExecute(20), Is.False);
        await vm.LoadInitialSongsAsync(CancellationToken.None);
        vm.CompleteSongsPresentation(vm.SongsPresentationGeneration);
        Assert.That(vm.Songs, Has.Count.EqualTo(45));
        Assert.That(vm.Songs.First().Name, Is.EqualTo("129"));
        Assert.That(vm.Songs.Last().Name, Is.EqualTo("085"));
        await vm.LoadMoreSongsCommand.ExecuteAsync(16);
        Assert.That(vm.Songs, Has.Count.EqualTo(61));
        Assert.That(vm.Songs.Last().Name, Is.EqualTo("069"));
        Assert.That(vm.IsProcessing, Is.False);
        Assert.That(vm.CanInteract, Is.True);
        Assert.That(vm.HasMoreSongs, Is.True);
        Assert.That(vm.LoadMoreSongsCommand.CanExecute(0), Is.False);
    }

    [Test]
    public async Task InitialSongDemandPublishesAllWhenFewerThanThirtySixExist()
    {
        var vm = CreateVM(new StubHomeFolderService
        {
            Songs = (_, _) => Task.FromResult<FolderSnapshot?>(new FolderSnapshot(
                Enumerable.Range(0, 7).Select(i =>
                    new FolderEntry($@"C:\Songs\{i}", $"{i}", DateTimeOffset.UnixEpoch.AddMinutes(i)))))
        });

        await vm.LoadAsync(CancellationToken.None);
        await vm.LoadInitialSongsAsync(CancellationToken.None);

        Assert.That(vm.Songs, Has.Count.EqualTo(7));
        Assert.That(vm.HasMoreSongs, Is.False);
    }

    [Test]
    public async Task ProjectsStillPublishOnlyRequestedPages()
    {
        var vm = CreateVM(new StubHomeFolderService
        {
            Projects = _ => Task.FromResult(new FolderSnapshot(Enumerable.Range(0, 130).Select(i =>
                new FolderEntry($@"C:\Projects\{i:D3}", $"{i:D3}", DateTimeOffset.UnixEpoch.AddMinutes(i)))))
        });
        await vm.LoadAsync(CancellationToken.None);
        Assert.That(vm.Projects, Has.Count.EqualTo(64));
        vm.CompleteSongsPresentation(vm.SongsPresentationGeneration);
        await vm.LoadMoreProjectsCommand.ExecuteAsync(null);
        Assert.That(vm.Projects, Has.Count.EqualTo(128));
        await vm.LoadMoreProjectsCommand.ExecuteAsync(null);
        Assert.That(vm.Projects, Has.Count.EqualTo(130));
        Assert.That(vm.HasMoreProjects, Is.False);
    }

    [Test]
    public async Task EmptySongsRefreshRebuildsOnlySongsSnapshot()
    {
        var projectCalls = 0;
        var songCalls = 0;
        var folders = new StubHomeFolderService
        {
            Projects = _ =>
            {
                projectCalls++;
                return Task.FromResult(new FolderSnapshot(
                    [new FolderEntry(@"C:\Projects\One", "One", DateTimeOffset.MinValue)]));
            },
            Songs = (_, _) =>
            {
                songCalls++;
                return Task.FromResult<FolderSnapshot?>(songCalls == 1
                    ? new FolderSnapshot([])
                    : new FolderSnapshot([new FolderEntry(@"C:\Songs\One", "One", DateTimeOffset.MinValue)]));
            }
        };
        var vm = CreateVM(folders);
        await vm.LoadAsync(CancellationToken.None);
        Assert.That(vm.CanRetrySongs, Is.True);
        vm.CompleteSongsPresentation(vm.SongsPresentationGeneration);

        await vm.RetrySongsCommand.ExecuteAsync(null);

        Assert.Multiple(() =>
        {
            Assert.That(projectCalls, Is.EqualTo(1));
            Assert.That(songCalls, Is.EqualTo(2));
            Assert.That(vm.Projects, Has.Count.EqualTo(1));
            Assert.That(vm.Songs, Is.Empty);
            Assert.That(vm.HasMoreSongs, Is.True);
            Assert.That(vm.CanRetrySongs, Is.False);
        });
    }

    [Test]
    public async Task PendingLoadCancellationRestoresBusyState()
    {
        var pending = new TaskCompletionSource<FolderSnapshot?>();
        using var cancellation = new CancellationTokenSource();
        var vm = CreateVM(new StubHomeFolderService { Songs = (_, token) => pending.Task.WaitAsync(token) });
        var load = vm.LoadAsync(cancellation.Token);
        Assert.That(vm.IsLoading, Is.True);
        cancellation.Cancel();
        Assert.CatchAsync<OperationCanceledException>(async () => await load);
        await Task.Yield();
        Assert.That(vm.IsLoading, Is.False);
        Assert.That(vm.IsProcessing, Is.False);
        Assert.That(vm.IsInteractionBlocked, Is.False);
        Assert.That(vm.FatalError, Is.Null);
    }

    [Test]
    public async Task ProjectFailureCancelsPendingSongsAndReportsOneFatalError()
    {
        var vm = CreateVM(new StubHomeFolderService
        {
            Projects = _ => throw new IOException("Disk unavailable"),
            Songs = (_, token) => Task.Delay(Timeout.Infinite, token).ContinueWith<FolderSnapshot?>(_ => null)
        });
        await vm.LoadAsync(CancellationToken.None);
        Assert.That(vm.FatalError, Does.Contain("Disk unavailable"));
        Assert.That(vm.RequiresShutdown, Is.True);
        Assert.That(vm.IsLoading, Is.False);
        Assert.That(vm.IsProcessing, Is.False);
        Assert.That(vm.IsInteractionBlocked, Is.False);
    }

    [Test]
    public async Task CancellationDuringDemandStopsPublicationAndClearsBusyState()
    {
        using var cancellation = new CancellationTokenSource();
        var vm = CreateVM(new StubHomeFolderService
        {
            Songs = (_, _) => Task.FromResult<FolderSnapshot?>(new FolderSnapshot(Enumerable.Range(0, 130)
                .Select(i => new FolderEntry($@"C:\Songs\{i}", $"{i}", DateTimeOffset.MinValue))))
        });
        await vm.LoadAsync(cancellation.Token);
        vm.CompleteSongsPresentation(vm.SongsPresentationGeneration);
        vm.Songs.CollectionChanged += (_, _) =>
        {
            if (vm.Songs.Count == 20) cancellation.Cancel();
        };
        await vm.LoadMoreSongsCommand.ExecuteAsync(64);
        Assert.That(vm.Songs, Has.Count.EqualTo(20));
        Assert.That(vm.IsProcessing, Is.False);
        Assert.That(vm.IsInteractionBlocked, Is.False);
        Assert.That(vm.IsLoadingSongs, Is.False);
    }

    [Test]
    public async Task SongsRetryRebuildsSnapshotWithoutPublishingRows()
    {
        var folders = new StubHomeFolderService();
        var vm = CreateVM(folders);
        await vm.LoadAsync(CancellationToken.None);
        vm.ReportDemandFailure(new IOException("Retry songs"));
        var pending = new TaskCompletionSource<FolderSnapshot?>(TaskCreationOptions.RunContinuationsAsynchronously);
        folders.Songs = (_, token) => pending.Task.WaitAsync(token);
        var retry = vm.RetrySongsCommand.ExecuteAsync(null);
        try
        {
            Assert.That(vm.IsProcessing, Is.True);
            Assert.That(vm.CanInteract, Is.False);
        }
        finally
        {
            pending.TrySetResult(new FolderSnapshot(Enumerable.Range(0, 130)
                .Select(i => new FolderEntry($@"C:\Songs\{i}", $"{i}", DateTimeOffset.MinValue))));
            await retry;
        }
        Assert.That(vm.Songs, Is.Empty);
        Assert.That(vm.HasMoreSongs, Is.True);
        Assert.That(vm.IsProcessing, Is.True);
        vm.CompleteSongsPresentation(vm.SongsPresentationGeneration);
        Assert.That(vm.IsProcessing, Is.False);
        Assert.That(vm.IsInteractionBlocked, Is.False);
        Assert.That(vm.CanRetrySongs, Is.False);
    }

    [Test]
    public async Task SongsFailureClearsProcessing()
    {
        var vm = CreateVM(new StubHomeFolderService { Songs = (_, _) => throw new IOException("Song read failed") });
        await vm.LoadAsync(CancellationToken.None);
        Assert.That(vm.FatalError, Does.Contain("Song read failed"));
        Assert.That(vm.RequiresShutdown, Is.True);
        Assert.That(vm.IsProcessing, Is.False);
        Assert.That(vm.IsInteractionBlocked, Is.False);
    }
}
