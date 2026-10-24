using CommunityToolkit.Mvvm.Messaging;
using Onset.Resources.DataModels;
using Onset.ViewModels.Shell.HomePage;

namespace Onset.Test.Presentation;

public sealed class HomeProjectBrowserTests
{
    private static FolderEntry Entry(string name, bool pinned = false, int minutes = 0) => new(name, name, default)
        { IsPinned = pinned, LastChanged = DateTimeOffset.UnixEpoch.AddMinutes(minutes), HasProjectMetadata = true };

    private static async Task<HomePageVM> Ready(IEnumerable<FolderEntry> entries, StubProjectFileService? files = null,
        StubProjectRemovalDialogService? dialogs = null)
    {
        var vm = new HomePageVM(new StubHomeFolderService { Projects = _ => Task.FromResult(FolderSnapshot.ForProjects(entries, true)) },
            new StubAppSettingsService(), new StubInstallationDialogService(), new StubProjectCreationDialogService(),
            new ImmediateUiScheduler(), files ?? new StubProjectFileService((_, _, _) => throw new AssertionException("Unexpected creation")),
            new WeakReferenceMessenger(), dialogs ?? new StubProjectRemovalDialogService());
        await vm.LoadAsync(default);
        await vm.LoadInitialSongsAsync(default);
        vm.CompleteSongsPresentation(vm.SongsPresentationGeneration);
        return vm;
    }

    [Test]
    public async Task SearchFindsUnpublishedProjectsAnywhereAndRemovesAllHeadings()
    {
        var rows = Enumerable.Range(0, 90).Select(i => Entry(i == 0 ? "middleMATCHend" : $"project{i}", i % 2 == 0, i)).ToArray();
        var vm = await Ready(rows);
        try
        {
            Assert.That(vm.Projects.Count, Is.EqualTo(64));
            Assert.That(vm.ProjectItems.Where(x => x.IsHeading).Select(x => x.Heading), Is.EqualTo(new[] { "Pinned", "History" }));
            vm.ProjectSearchText = " match "; await vm.SearchProjectsCommand.ExecutionTask!;
            Assert.That(vm.Projects.Single().Name, Is.EqualTo("middleMATCHend"));
            Assert.That(vm.ProjectItems.All(x => !x.IsHeading), Is.True);
            vm.ProjectSearchText = " "; await vm.SearchProjectsCommand.ExecutionTask!;
            Assert.That(vm.ProjectItems.Count(x => x.IsHeading), Is.EqualTo(2));
        }
        finally { vm.Deactivate(); }
    }

    [Test]
    public async Task PinPersistsOnlyAfterSuccessAndDeleteRequiresConfirmation()
    {
        var row = Entry("project");
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var deleted = false;
        var files = new StubProjectFileService((_, _, _) => throw new AssertionException("Unexpected creation"))
        {
            Pin = (_, _, _) => release.Task,
            Delete = (_, _) => { deleted = true; return Task.CompletedTask; }
        };
        var dialogs = new StubProjectRemovalDialogService();
        var vm = await Ready([row], files, dialogs);
        try
        {
            var pin = vm.ToggleProjectPinCommand.ExecuteAsync(row);
            Assert.That(vm.Projects.Single().IsPinned, Is.False);
            Assert.That(vm.OpenProjectCommand.CanExecute(row), Is.False);
            release.SetResult(); await pin;
            Assert.That(vm.Projects.Single().IsPinned, Is.True);
            Assert.That(vm.Projects.Single().LastChanged, Is.EqualTo(row.LastChanged));
            await vm.DeleteProjectCommand.ExecuteAsync(vm.Projects.Single());
            Assert.That(deleted, Is.False);
            dialogs.Confirm = (_, _) => Task.FromResult(true);
            await vm.DeleteProjectCommand.ExecuteAsync(vm.Projects.Single());
            Assert.That(deleted, Is.True);
            Assert.That(vm.Projects, Is.Empty);
        }
        finally { release.TrySetResult(); vm.Deactivate(); }
    }

    [Test]
    public async Task LatestQueryWinsAndFailedDeletionKeepsInventory()
    {
        var files = new StubProjectFileService((_, _, _) => throw new AssertionException("Unexpected creation"))
            { Delete = (_, _) => throw new IOException("Locked") };
        var vm = await Ready([Entry("first"), Entry("second")], files,
            new StubProjectRemovalDialogService { Confirm = (_, _) => Task.FromResult(true) });
        try
        {
            vm.ProjectSearchText = "first";
            var first = vm.SearchProjectsCommand.ExecutionTask!;
            vm.ProjectSearchText = "second";
            await Task.WhenAll(first, vm.SearchProjectsCommand.ExecutionTask!);
            Assert.That(vm.Projects.Single().Name, Is.EqualTo("second"));
            await vm.DeleteProjectCommand.ExecuteAsync(vm.Projects.Single());
            Assert.That(vm.Projects.Single().Name, Is.EqualTo("second"));
            vm.ProjectSearchText = "first";
            var late = vm.SearchProjectsCommand.ExecutionTask!;
            vm.Deactivate();
            var count = vm.Projects.Count;
            await late;
            Assert.That(vm.Projects.Count, Is.EqualTo(count));
        }
        finally { vm.Deactivate(); }
    }

    [Test]
    public async Task FailedPinPreservesStateAndDeactivationDisablesActions()
    {
        var row = Entry("project");
        var files = new StubProjectFileService((_, _, _) => throw new AssertionException("Unexpected creation"))
            { Pin = (_, _, _) => throw new IOException("Disk full") };
        var vm = await Ready([row], files);
        await vm.ToggleProjectPinCommand.ExecuteAsync(row);
        Assert.That(vm.Projects.Single().IsPinned, Is.False);
        vm.Deactivate();
        Assert.That(vm.ToggleProjectPinCommand.CanExecute(row), Is.False);
        Assert.That(vm.DeleteProjectCommand.CanExecute(row), Is.False);
    }
}
