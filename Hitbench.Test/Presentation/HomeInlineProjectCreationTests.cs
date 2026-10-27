using CommunityToolkit.Mvvm.Messaging;
using Hitbench.Resources.DataModels;
using Hitbench.Resources.Messages;
using Hitbench.ViewModels.Shell.HomePage;

namespace Hitbench.Test.Presentation;

public sealed class HomeInlineProjectCreationTests
{
    [Test]
    public async Task LateNameReadCannotReopenOldEditor()
    {
        var pending = new TaskCompletionSource<IReadOnlySet<string>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var files = new StubProjectFileService((_, _, _) => throw new AssertionException("Unexpected save"))
            { GetNames = _ => pending.Task };
        var vm = Create((_, _, _) => throw new AssertionException("Unexpected save"), new WeakReferenceMessenger(), files);
        try
        {
            vm.SelectedMapset = Mapset;
            var showing = vm.ShowProjectCreationCommand.ExecuteAsync(null);
            Assert.That(vm.IsLoadingProjectNames, Is.True);
            Assert.That(vm.CreateProjectCommand.CanExecute(null), Is.False);
            vm.SelectedMapset = null;
            pending.SetResult(new HashSet<string>());
            await showing;
            Assert.That(vm.IsProjectNameEditorVisible, Is.False);
            Assert.That(vm.IsLoadingProjectNames, Is.False);
        }
        finally { pending.TrySetResult(new HashSet<string>()); vm.Deactivate(); }
    }

    [Test]
    public async Task CorrectingDraftClearsErrorsAndSubmissionRechecksNames()
    {
        var files = new StubProjectFileService((_, _, _) => throw new AssertionException("Duplicate persisted"));
        var vm = Create((_, _, _) => throw new AssertionException("Unexpected save"), new WeakReferenceMessenger(), files);
        try
        {
            vm.SelectedMapset = Mapset;
            await vm.ShowProjectCreationCommand.ExecuteAsync(null);
            vm.DraftProjectName = "CON";
            Assert.That(vm.GetErrors(nameof(vm.DraftProjectName)).Cast<string>(), Is.Not.Empty);
            vm.DraftProjectName = "fresh";
            Assert.That(vm.GetErrors(nameof(vm.DraftProjectName)).Cast<string>(), Is.Empty);
            files.GetNames = _ => Task.FromResult<IReadOnlySet<string>>(new HashSet<string> { "FRESH" });
            await vm.CreateProjectCommand.ExecuteAsync(null);
            Assert.That(vm.HasErrors, Is.True);
            Assert.That(vm.CreateProjectCommand.CanExecute(null), Is.False);
        }
        finally { vm.Deactivate(); }
    }

    [TestCase("")]
    [TestCase(" ")]
    [TestCase("CON")]
    [TestCase("A:B")]
    [TestCase("name.")]
    [TestCase("name ")]
    [TestCase("EXISTING")]
    public async Task InvalidDraftCannotCreate(string name)
    {
        var vm = Create((_, _, _) => throw new AssertionException("Invalid draft persisted"), new WeakReferenceMessenger());
        try
        {
            vm.SelectedMapset = Mapset;
            await vm.ShowProjectCreationCommand.ExecuteAsync(null);
            vm.DraftProjectName = name;
            Assert.That(vm.HasErrors, Is.True);
            Assert.That(vm.CreateProjectCommand.CanExecute(null), Is.False);
        }
        finally { vm.Deactivate(); }
    }

    [Test]
    public async Task CreatePublishesOnlyAfterSaveAndPreventsOverlap()
    {
        var pending = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var messenger = new WeakReferenceMessenger();
        var messages = new List<string>();
        messenger.Register<ProjectCreatedMessage>(messages, (_, m) => messages.Add(m.ProjectPath));
        var vm = Create((folder, name, _) =>
        {
            Assert.That(folder.FullName, Is.EqualTo(Mapset.FullName));
            Assert.That(name, Is.EqualTo("My project"));
            return pending.Task;
        }, messenger);
        try
        {
            vm.SelectedMapset = Mapset;
            await vm.ShowProjectCreationCommand.ExecuteAsync(null);
            vm.DraftProjectName = "My project";
            Assert.That(vm.HasErrors, Is.False);
            var creating = vm.CreateProjectCommand.ExecuteAsync(null);
            Assert.That(vm.IsCreatingProject, Is.True);
            Assert.That(vm.CreateProjectCommand.CanExecute(null), Is.False);
            Assert.That(messages, Is.Empty);
            pending.SetResult("saved.json");
            await creating;
            Assert.That(messages, Is.EqualTo(new[] { "saved.json" }));
        }
        finally { pending.TrySetResult("saved.json"); vm.Deactivate(); }
    }

    [Test]
    public async Task SelectionChangeAndCancelDiscardDraft()
    {
        var vm = Create((_, _, _) => throw new AssertionException("Unexpected save"), new WeakReferenceMessenger());
        try
        {
            vm.SelectedMapset = Mapset;
            await vm.ShowProjectCreationCommand.ExecuteAsync(null);
            vm.DraftProjectName = "draft";
            vm.CancelProjectCreationCommand.Execute(null);
            Assert.That(vm.IsProjectNameEditorVisible, Is.False);
            Assert.That(vm.DraftProjectName, Is.Empty);
            await vm.ShowProjectCreationCommand.ExecuteAsync(null);
            vm.DraftProjectName = "draft";
            vm.SelectedMapset = null;
            Assert.That(vm.IsProjectNameEditorVisible, Is.False);
            Assert.That(vm.DraftProjectName, Is.Empty);
        }
        finally { vm.Deactivate(); }
    }

    internal static readonly FolderEntry Mapset = new(@"C:\Songs\Selected", "Selected", default);
    internal static HomePageVM Create(Func<DirectoryInfo, string, CancellationToken, Task<string>> save,
        WeakReferenceMessenger messenger, StubProjectFileService? files = null) =>
        new(new StubHomeFolderService(), new StubAppSettingsService(), new StubInstallationDialogService(),
            new ImmediateUiScheduler(),
            files ?? new StubProjectFileService(save, new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "existing" }), messenger);
}
