using CommunityToolkit.Mvvm.Messaging;
using osuHitsounder.Resources.DataModels;
using osuHitsounder.Resources.Messages;
using osuHitsounder.Services.FileSystem;
using osuHitsounder.ViewModels.Popups;

namespace osuHitsounder.Test.Presentation;

public sealed class ManageSamplesPopupVMTests
{
    [TestCase(true)][TestCase(false)]
    public async Task DeleteRequiresConfirmationAndRetainsRowsOnFailure(bool confirmed)
    {
        var files = new StubSampleFileService(["hit.wav"]) { Delete = (_, _, _) => Task.FromException(new IOException("Denied")) };
        var vm = new ManageSamplesPopupVM(new("project.json", null, Guid.NewGuid()), files, new(files),
            new StubSampleFileDialogService(Array.Empty<string>()), new StubSampleRemovalDialogService { Confirm = (_, _) => Task.FromResult(confirmed) },
            new WeakReferenceMessenger(), new ImmediateUiScheduler());
        await vm.InitializeAsync(default); await vm.DeleteCommand.ExecuteAsync(vm.Entries.Single());
        Assert.That(vm.Entries.Single().FileName, Is.EqualTo("hit.wav"));
        if (confirmed) Assert.That(vm.ErrorMessage, Is.EqualTo("Denied"));
        Assert.That(vm.IsBusy, Is.False); await vm.DeactivateAsync();
    }
    [Test] public async Task CancellationAfterSuccessfulRenameSettlesBeforeDeactivation()
    {
        var pending = new TaskCompletionSource<string>();
        var files = new StubSampleFileService(["hit.wav"]) { Rename = (_, _, _, _) => pending.Task };
        var messenger = new WeakReferenceMessenger(); var received = new List<ProjectSamplesChangedMessage>();
        var recipient = new object(); messenger.Register<ProjectSamplesChangedMessage>(recipient, (_, m) => received.Add(m));
        var vm = new ManageSamplesPopupVM(new("project.json", null, Guid.NewGuid()), files, new(files),
            new StubSampleFileDialogService(Array.Empty<string>()), new StubSampleRemovalDialogService(), messenger, new ImmediateUiScheduler());
        await vm.InitializeAsync(default); await vm.EditCommand.ExecuteAsync(vm.Entries.Single()); vm.DraftStem = "kick";
        var committing = vm.CommitRenameCommand.ExecuteAsync(null); var closing = vm.DeactivateAsync();
        Assert.That(closing.IsCompleted, Is.False);
        pending.SetResult("kick.wav"); await committing; await closing;
        Assert.That(received.Any(m => m.NewFileName == "kick.wav"), Is.True);
    }
    [Test] public async Task InvalidDraftRefusesCloseAndDoesNotRename()
    {
        var files = new StubSampleFileService(["hit.wav"]) { Rename = (_, _, _, _) => throw new AssertionException("Invalid rename executed") };
        var vm = new ManageSamplesPopupVM(new("project.json", null, Guid.NewGuid()), files, new(files),
            new StubSampleFileDialogService(Array.Empty<string>()), new StubSampleRemovalDialogService(), new WeakReferenceMessenger(), new ImmediateUiScheduler());
        await vm.InitializeAsync(default); await vm.EditCommand.ExecuteAsync(vm.Entries.Single()); vm.DraftStem = "CON";
        Assert.That(await vm.PrepareToCloseAsync(), Is.False); Assert.That(vm.HasErrors, Is.True);
        vm.CancelEditCommand.Execute(null); Assert.That(await vm.PrepareToCloseAsync(), Is.True); await vm.DeactivateAsync();
    }
    [Test] public async Task RenamePublishesOnlyAfterSuccessAndSurvivesRefreshFailure()
    {
        var files = new StubSampleFileService(["hit.wav"]);
        var messenger = new WeakReferenceMessenger();
        var received = new List<ProjectSamplesChangedMessage>();
        var recipient = new object(); messenger.Register<ProjectSamplesChangedMessage>(recipient, (_, m) => received.Add(m));
        var vm = new ManageSamplesPopupVM(new("project.json", null, Guid.NewGuid()), files, new(files),
            new StubSampleFileDialogService(Array.Empty<string>()), new StubSampleRemovalDialogService(), messenger, new ImmediateUiScheduler());
        await vm.InitializeAsync(default); received.Clear();
        await vm.EditCommand.ExecuteAsync(vm.Entries.Single());
        vm.DraftStem = "kick";
        var pending = new TaskCompletionSource<string>(); files.Rename = (_, _, _, _) => pending.Task;
        files.List = (_, _) => Task.FromException<IReadOnlyList<string>>(new IOException("Refresh failed"));
        var commit = vm.CommitRenameCommand.ExecuteAsync(null);
        Assert.That(received, Is.Empty);
        pending.SetResult("kick.wav"); await commit;
        Assert.That(received.Single().NewFileName, Is.EqualTo("kick.wav"));
        Assert.That(vm.Entries.Single().FileName, Is.EqualTo("kick.wav"));
        Assert.That(vm.ErrorMessage, Does.Contain("Refresh failed"));
        Assert.That(vm.IsBusy, Is.False);
        await vm.DeactivateAsync();
    }
    [Test] public async Task CancelEditAndDeleteConfirmationPreserveSample()
    {
        var files = new StubSampleFileService(["hit.wav"]);
        var vm = new ManageSamplesPopupVM(new("project.json", null, Guid.NewGuid()), files, new(files),
            new StubSampleFileDialogService(Array.Empty<string>()), new StubSampleRemovalDialogService(), new WeakReferenceMessenger(), new ImmediateUiScheduler());
        await vm.InitializeAsync(default);
        await vm.EditCommand.ExecuteAsync(vm.Entries.Single()); vm.DraftStem = "kick";
        vm.CancelEditCommand.Execute(null);
        await vm.DeleteCommand.ExecuteAsync(vm.Entries.Single());
        Assert.That(vm.Entries.Single().FileName, Is.EqualTo("hit.wav"));
        Assert.That(vm.ActiveEdit, Is.Null);
        await vm.DeactivateAsync();
    }
}

