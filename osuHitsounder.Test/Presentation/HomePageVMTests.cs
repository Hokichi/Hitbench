using CommunityToolkit.Mvvm.Messaging;
using osuHitsounder.Resources.DataModels;
using osuHitsounder.ViewModels.Shell.HomePage;

namespace osuHitsounder.Test.Presentation;

public class HomePageVMTests
{
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
        vm.Projects.CollectionChanged += (_, _) => published.TrySetResult();
        var load = vm.LoadAsync(CancellationToken.None);
        await published.Task.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.That(vm.Projects.Single().Name, Is.EqualTo("project"));
        Assert.That(load.IsCompleted, Is.False);
        pending.SetResult(new FolderSnapshot([]));
        await load;
        Assert.That(vm.ProjectsStatus, Is.Empty);
    }

    [Test]
    public async Task SongSnapshotWaitsForExplicitViewportSizedDemand()
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
        Assert.That(vm.LoadMoreSongsCommand.CanExecute(20), Is.True);
        await vm.LoadMoreSongsCommand.ExecuteAsync(20);
        Assert.That(vm.Songs, Has.Count.EqualTo(20));
        Assert.That(vm.Songs.First().Name, Is.EqualTo("129"));
        Assert.That(vm.Songs.Last().Name, Is.EqualTo("110"));
        await vm.LoadMoreSongsCommand.ExecuteAsync(16);
        Assert.That(vm.Songs, Has.Count.EqualTo(36));
        Assert.That(vm.Songs.Last().Name, Is.EqualTo("094"));
        Assert.That(vm.IsProcessing, Is.False);
        Assert.That(vm.CanInteract, Is.True);
        Assert.That(vm.HasMoreSongs, Is.True);
        Assert.That(vm.LoadMoreSongsCommand.CanExecute(0), Is.False);
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
