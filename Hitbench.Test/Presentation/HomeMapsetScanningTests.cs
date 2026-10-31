using CommunityToolkit.Mvvm.Messaging;
using Hitbench.Resources.DataModels;
using Hitbench.ViewModels.Shell.HomePage;

namespace Hitbench.Test.Presentation;

public sealed class HomeMapsetScanningTests
{
    [Test]
    public async Task SearchSupersedingPublicationReleasesOldBusyOwnership()
    {
        var vm = Create(new StubHomeFolderService { Songs = (_, _) => Task.FromResult<FolderSnapshot?>(new FolderSnapshot(
            Enumerable.Range(0, 100).Select(i => new FolderEntry(i.ToString(), "map", default)))) });
        try
        {
            await vm.LoadAsync(default);
            await vm.LoadInitialSongsAsync(default);
            vm.CompleteSongsPresentation(vm.SongsPresentationGeneration);
            var superseded = false;
            vm.Songs.CollectionChanged += (_, _) =>
            {
                if (superseded) return;
                superseded = true;
                vm.SongSearchText = "map";
            };
            await vm.LoadMoreSongsCommand.ExecuteAsync(10);
            await vm.SearchSongsCommand.ExecutionTask!;
            Assert.That(vm.IsLoadingSongs, Is.False);
            Assert.That(vm.CompleteSongsPresentation(vm.SongsPresentationGeneration), Is.True);
        }
        finally { vm.Deactivate(); }
    }
    [TestCase(false)]
    [TestCase(true)]
    public async Task FailedOrCancelledReloadRetainsInventoryBehindPreparation(bool cancel)
    {
        var entry = new FolderEntry("one", "one", default);
        var pending = new TaskCompletionSource<FolderSnapshot?>();
        var folders = new StubHomeFolderService { Songs = (_, _) => Task.FromResult<FolderSnapshot?>(new FolderSnapshot([entry])) };
        var vm = Create(folders);
        try
        {
            await vm.LoadAsync(default);
            await vm.LoadInitialSongsAsync(default);
            vm.CompleteSongsPresentation(vm.SongsPresentationGeneration);
            var oldGeneration = vm.SongsPresentationGeneration;
            folders.ReloadSongs = (_, token) => pending.Task.WaitAsync(token);
            var reload = vm.ReloadSongsCommand.ExecuteAsync(null);
            Assert.That(vm.CanSelectMapset, Is.False);
            Assert.That(vm.CompleteSongsPresentation(oldGeneration), Is.False);
            if (cancel) vm.ReloadSongsCommand.Cancel();
            else pending.SetException(new IOException("reload failed"));
            await reload;
            Assert.That(vm.IsScanningMapsets, Is.False);
            Assert.That(vm.IsAwaitingSongsPresentation, Is.True);
            Assert.That(vm.CanSelectMapset, Is.False);
            Assert.That(vm.CompleteSongsPresentation(vm.SongsPresentationGeneration), Is.True);
            Assert.That(vm.CanSelectMapset, Is.True);
        }
        finally { vm.Deactivate(); }
    }

    [Test]
    public async Task SuspendAndLaterPagingUseDifferentReadinessBoundaries()
    {
        var vm = Create(new StubHomeFolderService { Songs = (_, _) => Task.FromResult<FolderSnapshot?>(new FolderSnapshot(
            Enumerable.Range(0, 100).Select(i => new FolderEntry(i.ToString(), "map", default)))) });
        try
        {
            await vm.LoadAsync(default);
            await vm.LoadInitialSongsAsync(default);
            vm.CompleteSongsPresentation(vm.SongsPresentationGeneration);
            vm.Songs.CollectionChanged += (_, _) => Assert.That(vm.CanSelectMapset, Is.True);
            await vm.LoadMoreSongsCommand.ExecuteAsync(10);
            vm.SuspendSongsPresentation();
            Assert.That(vm.CanSelectMapset, Is.False);
            Assert.That(vm.CompleteSongsPresentation(vm.SongsPresentationGeneration), Is.True);
            Assert.That(vm.CompleteSongsPresentation(vm.SongsPresentationGeneration), Is.False);
        }
        finally { vm.Deactivate(); }
    }
    [Test]
    public async Task HiddenMapsetsRejectDirectSelectionUntilPresentation()
    {
        var entry = new FolderEntry("one", "one", default);
        var vm = Create(new StubHomeFolderService { Songs = (_, _) => Task.FromResult<FolderSnapshot?>(new FolderSnapshot([entry])) });
        try
        {
            await vm.LoadAsync(default);
            await vm.LoadInitialSongsAsync(default);
            Assert.That(vm.CanSelectMapset, Is.False);
            Assert.That(vm.SelectMapsetCommand.CanExecute(entry), Is.False);
            vm.SelectMapsetCommand.Execute(entry);
            Assert.That(vm.SelectedMapset, Is.Null);
            vm.CompleteSongsPresentation(vm.SongsPresentationGeneration - 1);
            Assert.That(vm.CanSelectMapset, Is.False);
            vm.CompleteSongsPresentation(vm.SongsPresentationGeneration);
            Assert.That(vm.CanSelectMapset, Is.True);
            vm.SelectMapsetCommand.Execute(entry);
            Assert.That(vm.SelectedMapset, Is.SameAs(entry));
        }
        finally { vm.Deactivate(); }
    }
    [Test]
    public async Task FolderPickerDoesNotScanUntilConfirmationAndLateLoadCannotReactivateHome()
    {
        var selection = new TaskCompletionSource<IReadOnlyList<string>>();
        var snapshot = new TaskCompletionSource<FolderSnapshot?>();
        var scanStarted = new TaskCompletionSource();
        var folders = new StubHomeFolderService();
        var picker = new StubSampleFileDialogService([]) { Selection = _ => selection.Task };
        var vm = new HomePageVM(folders, new StubAppSettingsService(), new StubInstallationDialogService(),
            new ImmediateUiScheduler(), new StubProjectFileService((_, _, _) => Task.FromResult("saved.json")),
            new WeakReferenceMessenger(), picker: picker);
        try
        {
            await vm.LoadAsync(default);
            vm.CompleteSongsPresentation(vm.SongsPresentationGeneration);
            folders.ReloadSongs = (_, _) => { scanStarted.TrySetResult(); return snapshot.Task; }; // Deliberately ignores cancellation.
            var locate = vm.LocateMapsetsDirectoryCommand.ExecuteAsync(null);
            Assert.That(vm.IsScanningMapsets, Is.False);
            selection.SetResult([@"C:\maps"]);
            await scanStarted.Task.WaitAsync(TimeSpan.FromSeconds(3));
            Assert.That(vm.IsScanningMapsets, Is.True);
            vm.Deactivate();
            snapshot.SetResult(new FolderSnapshot([new FolderEntry("late", "late", default)]));
            await locate;
            Assert.That(vm.IsScanningMapsets, Is.False);
            Assert.That(vm.Songs, Is.Empty);
        }
        finally { selection.TrySetResult([]); snapshot.TrySetResult(new FolderSnapshot([])); vm.Deactivate(); }
    }

    internal static HomePageVM Create(StubHomeFolderService folders) => new(folders,
        new StubAppSettingsService(), new StubInstallationDialogService(), new ImmediateUiScheduler(),
        new StubProjectFileService((_, _, _) => Task.FromResult("saved.json")), new WeakReferenceMessenger());

    [Test]
    public async Task InitialScanRemainsActiveUntilCurrentPresentationCompletes()
    {
        var pending = new TaskCompletionSource<FolderSnapshot?>();
        var vm = Create(new StubHomeFolderService { Songs = (_, _) => pending.Task });
        try
        {
            var loading = vm.LoadAsync(default);
            Assert.That(vm.IsScanningMapsets, Is.True);
            pending.SetResult(new FolderSnapshot([new FolderEntry("one", "one", default)]));
            await loading;
            await vm.LoadInitialSongsAsync(default);
            vm.CompleteSongsPresentation(vm.SongsPresentationGeneration - 1);
            Assert.That(vm.IsScanningMapsets, Is.True);
            vm.CompleteSongsPresentation(vm.SongsPresentationGeneration);
            Assert.That(vm.IsScanningMapsets, Is.False);
        }
        finally { vm.Deactivate(); }
    }

    [TestCase(false)] [TestCase(true)]
    public async Task FailureAndCancellationEndScanning(bool cancel)
    {
        var pending = new TaskCompletionSource<FolderSnapshot?>();
        var vm = Create(new StubHomeFolderService { Songs = (_, token) => pending.Task.WaitAsync(token) });
        try
        {
            var loading = vm.LoadAsync(default);
            Assert.That(vm.IsScanningMapsets, Is.True);
            if (cancel) vm.Deactivate(); else pending.SetException(new IOException("scan failed"));
            try { await loading; } catch (OperationCanceledException) when (cancel) { }
            Assert.That(vm.IsScanningMapsets, Is.False);
        }
        finally { vm.Deactivate(); }
    }

    [Test]
    public async Task ReloadOwnsScanButSearchAndPagingDoNot()
    {
        var folders = new StubHomeFolderService { Songs = (_, _) => Task.FromResult<FolderSnapshot?>(new FolderSnapshot(
            Enumerable.Range(0, 100).Select(i => new FolderEntry(i.ToString(), "map", default)))) };
        var vm = Create(folders);
        try
        {
            await vm.LoadAsync(default); await vm.LoadInitialSongsAsync(default);
            vm.CompleteSongsPresentation(vm.SongsPresentationGeneration);
            await vm.LoadMoreSongsCommand.ExecuteAsync(10);
            Assert.That(vm.IsScanningMapsets, Is.False);
            vm.SongSearchText = "map";
            await vm.SearchSongsCommand.ExecutionTask!;
            Assert.That(vm.IsScanningMapsets, Is.False);
            vm.CompleteSongsPresentation(vm.SongsPresentationGeneration);
            var pending = new TaskCompletionSource<FolderSnapshot?>();
            folders.ReloadSongs = (_, _) => pending.Task;
            var reload = vm.ReloadSongsCommand.ExecuteAsync(null);
            Assert.That(vm.IsScanningMapsets, Is.True);
            pending.SetResult(new FolderSnapshot([])); await reload;
            vm.CompleteSongsPresentation(vm.SongsPresentationGeneration);
            Assert.That(vm.IsScanningMapsets, Is.False);
        }
        finally { vm.Deactivate(); }
    }
}
