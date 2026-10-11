using CommunityToolkit.Mvvm.Messaging;
using osuHitsounder.Resources.DataModels;
using osuHitsounder.Resources.Messages;
using osuHitsounder.Services.FileSystem;
using osuHitsounder.ViewModels.Popups;

namespace osuHitsounder.Test.Presentation;

public sealed class ManageSamplesPopupVMTests
{
    [Test] public async Task PreviewStartupKeepsEditAndDeleteEnabledAndTogglesPause()
    {
        var pending=new TaskCompletionSource(); var preview=new StubSamplePreviewService{Play=_=>pending.Task}; var files=new StubSampleFileService(["hit.wav"]);
        var vm=new ManageSamplesPopupVM(new("project.json",null,Guid.NewGuid()),files,new(files),new StubSampleFileDialogService(Array.Empty<string>()),new StubSampleRemovalDialogService(),new WeakReferenceMessenger(),new ImmediateUiScheduler(),preview);
        await vm.InitializeAsync(default); var entry=vm.Entries.Single(); var playing=vm.PlayCommand.ExecuteAsync(entry);
        Assert.That(vm.IsBusy,Is.False); Assert.That(vm.EditCommand.CanExecute(entry),Is.True); Assert.That(vm.DeleteCommand.CanExecute(entry),Is.True);
        pending.SetResult(); await playing; Assert.That(entry.IsPlaying,Is.True);
        await vm.PlayCommand.ExecuteAsync(entry); Assert.That(entry.IsPlaying,Is.False); Assert.That(preview.Playing,Is.False);
        await vm.DeactivateAsync();
    }
    [Test] public async Task PreviewCompletionRestoresPlayState()
    {
        var preview=new StubSamplePreviewService(); var files=new StubSampleFileService(["hit.wav"]);
        var vm=new ManageSamplesPopupVM(new("project.json",null,Guid.NewGuid()),files,new(files),new StubSampleFileDialogService(Array.Empty<string>()),new StubSampleRemovalDialogService(),new WeakReferenceMessenger(),new ImmediateUiScheduler(),preview);
        await vm.InitializeAsync(default); var entry=vm.Entries.Single(); await vm.PlayCommand.ExecuteAsync(entry);
        await preview.StopAsync(default);
        await StubWorkstationAudioService.WaitUntilAsync(()=>!entry.IsPlaying);
        Assert.That(vm.IsBusy,Is.False); await vm.DeactivateAsync();
    }
    [Test] public async Task PlayUsesProjectSampleAndRetiresPreviewBeforeRenameAndClose()
    {
        var preview=new StubSamplePreviewService(); var files=new StubSampleFileService(["hit.wav"]);
        var project=Path.Combine(Path.GetTempPath(),"preview-project","project.json");
        var vm=new ManageSamplesPopupVM(new(project,null,Guid.NewGuid()),files,new(files),new StubSampleFileDialogService(Array.Empty<string>()),
            new StubSampleRemovalDialogService(),new WeakReferenceMessenger(),new ImmediateUiScheduler(),preview);
        await vm.InitializeAsync(default); var entry=vm.Entries.Single();
        await vm.PlayCommand.ExecuteAsync(entry);
        Assert.That(preview.PlayedPaths.Single(),Is.EqualTo(Path.Combine(Path.GetDirectoryName(project)!,"Samples","hit.wav")));
        Assert.That(preview.Playing,Is.True);
        files.Rename=(_,_,_,_)=> { Assert.That(preview.Playing,Is.False);return Task.FromResult("kick.wav"); };
        files.List=(_,_)=>Task.FromResult<IReadOnlyList<string>>(["kick.wav"]);
        await vm.EditCommand.ExecuteAsync(entry); vm.DraftStem="kick"; await vm.CommitRenameCommand.ExecuteAsync(null);
        await vm.PlayCommand.ExecuteAsync(vm.Entries.Single()); await vm.DeactivateAsync();
        Assert.That(preview.Playing,Is.False); Assert.That(preview.Disposed,Is.True);
    }
    [Test] public async Task PlaybackFailureRestoresActionsAndShowsError()
    {
        var preview=new StubSamplePreviewService{Play=_=>Task.FromException(new IOException("Playback failed"))}; var files=new StubSampleFileService(["hit.wav"]);
        var vm=new ManageSamplesPopupVM(new("project.json",null,Guid.NewGuid()),files,new(files),new StubSampleFileDialogService(Array.Empty<string>()),
            new StubSampleRemovalDialogService(),new WeakReferenceMessenger(),new ImmediateUiScheduler(),preview);
        await vm.InitializeAsync(default); await vm.PlayCommand.ExecuteAsync(vm.Entries.Single());
        Assert.That(vm.ErrorMessage,Is.EqualTo("Playback failed")); Assert.That(vm.IsBusy,Is.False);
        Assert.That(vm.PlayCommand.CanExecute(vm.Entries.Single()),Is.True); await vm.DeactivateAsync();
    }
    [Test] public async Task ConfirmedDeletePublishesKnownRemovalDespiteRefreshFailure()
    {
        var files=new StubSampleFileService(["hit.wav"]); var messenger=new WeakReferenceMessenger(); var received=new List<ProjectSamplesChangedMessage>();
        var recipient=new object();messenger.Register<ProjectSamplesChangedMessage>(recipient,(_,m)=>received.Add(m));
        var vm=new ManageSamplesPopupVM(new("project.json",null,Guid.NewGuid()),files,new(files),new StubSampleFileDialogService(Array.Empty<string>()),
            new StubSampleRemovalDialogService{Confirm=(_,_)=>Task.FromResult(true)},messenger,new ImmediateUiScheduler());
        await vm.InitializeAsync(default); received.Clear(); files.List=(_,_)=>Task.FromException<IReadOnlyList<string>>(new IOException("Refresh failed"));
        await vm.DeleteCommand.ExecuteAsync(vm.Entries.Single());
        Assert.That(vm.Entries,Is.Empty); Assert.That(received.Single().OldFileName,Is.EqualTo("hit.wav")); Assert.That(received.Single().Files,Is.Empty);
        Assert.That(vm.ErrorMessage,Does.Contain("Refresh failed")); await vm.DeactivateAsync();
    }
    [Test] public async Task AddPreservesPartialBatchAndPassesProjectInitialDirectory()
    {
        var files=new StubSampleFileService([]){Import=(_,path,_)=>path=="bad.wav"?Task.FromException<string>(new IOException("Denied")):Task.FromResult(path)};
        var picker=new StubSampleFileDialogService(new[]{"hit.wav","bad.wav","kick.ogg"});
        var vm=new ManageSamplesPopupVM(new("project.json","beatmap",Guid.NewGuid()),files,new(files),picker,new StubSampleRemovalDialogService(),new WeakReferenceMessenger(),new ImmediateUiScheduler());
        await vm.InitializeAsync(default); files.List=(_,_)=>Task.FromResult<IReadOnlyList<string>>(["hit.wav","kick.ogg"]);
        await vm.AddNewSampleCommand.ExecuteAsync(null);
        Assert.That(picker.InitialDirectory,Is.EqualTo("beatmap")); Assert.That(vm.Entries.Select(e=>e.FileName),Is.EqualTo(new[]{"hit.wav","kick.ogg"}));
        Assert.That(vm.ErrorMessage,Does.Contain("bad.wav")); await vm.DeactivateAsync();
    }
    [Test] public async Task FailedCloseDuringRenameAllowsRetry()
    {
        var pending = new TaskCompletionSource<string>();
        var files = new StubSampleFileService(["hit.wav"]) { Rename=(_,_,_,_)=>pending.Task };
        var vm = new ManageSamplesPopupVM(new("project.json",null,Guid.NewGuid()),files,new(files),new StubSampleFileDialogService(Array.Empty<string>()),new StubSampleRemovalDialogService(),new WeakReferenceMessenger(),new ImmediateUiScheduler());
        await vm.InitializeAsync(default); await vm.EditCommand.ExecuteAsync(vm.Entries.Single()); vm.DraftStem="kick";
        var commit=vm.CommitRenameCommand.ExecuteAsync(null); var closing=vm.PrepareToCloseAsync();
        pending.SetException(new OperationCanceledException()); await commit;
        Assert.That(await closing,Is.False);
        files.Rename=(_,_,_,token)=> { token.ThrowIfCancellationRequested(); return Task.FromResult("kick.wav"); };
        files.List=(_,_)=>Task.FromResult<IReadOnlyList<string>>(["kick.wav"]);
        await vm.CommitRenameCommand.ExecuteAsync(null); Assert.That(vm.ActiveEdit,Is.Null); await vm.DeactivateAsync();
    }
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

