using CommunityToolkit.Mvvm.Messaging;
using Soundie.Resources.DataModels;
using Soundie.Resources.Messages;
using Soundie.Services.FileSystem;
using Soundie.ViewModels.Popups;

namespace Soundie.Test.Presentation;

public sealed class ManageSamplesPopupVMTests
{
    [Test]
    public async Task ManagerListsOnlyDirectSamples()
    {
        var vm = CreateManager(new StubSampleFileService(["kick.wav", "Piano/C3.wav"]));
        try
        {
            await vm.InitializeAsync(default);
            Assert.That(vm.Entries.Select(entry => entry.FileName), Is.EqualTo(new[] { "kick.wav" }));
        }
        finally { await vm.DeactivateAsync(); }
    }

    [Test]
    public async Task EditLocksOtherRowsAndMutations()
    {
        var files = new StubSampleFileService(["hit.wav", "kick.wav"]);
        var vm = CreateManager(files);
        try
        {
            await vm.InitializeAsync(default);
            var first = vm.Entries[0]; var second = vm.Entries[1];
            await vm.EditCommand.ExecuteAsync(first);
            Assert.That(vm.EditCommand.CanExecute(second), Is.False);
            Assert.That(vm.DeleteCommand.CanExecute(first), Is.False);
            Assert.That(vm.AddNewSampleCommand.CanExecute(null), Is.False);
            await vm.EditCommand.ExecuteAsync(second);
            Assert.That(vm.ActiveEdit, Is.SameAs(first));
            vm.CancelEditCommand.Execute(null);
            Assert.That(vm.EditCommand.CanExecute(second), Is.True);
            Assert.That(vm.DeleteCommand.CanExecute(first), Is.True);
            Assert.That(vm.AddNewSampleCommand.CanExecute(null), Is.True);
        }
        finally { await vm.DeactivateAsync(); }
    }

    [TestCase("")][TestCase(" ")][TestCase("CON")][TestCase("bad/name")]
    [TestCase("bad\\name")][TestCase("name.")][TestCase("name ")]
    public async Task InvalidDraftDisablesSaveUntilCorrected(string invalid)
    {
        var vm = CreateManager(new StubSampleFileService(["hit.wav"]));
        try
        {
            await vm.InitializeAsync(default); var row = vm.Entries.Single();
            await vm.EditCommand.ExecuteAsync(row); vm.DraftStem = invalid;
            Assert.That(vm.CommitRenameCommand.CanExecute(null), Is.False);
            Assert.That(vm.EditCommand.CanExecute(row), Is.False);
            Assert.That(vm.HasErrors, Is.True);
            Assert.That(vm.ErrorMessage, Is.Not.Null.And.Not.Empty);
            vm.DraftStem = "kick";
            Assert.That(vm.CommitRenameCommand.CanExecute(null), Is.True);
            Assert.That(vm.EditCommand.CanExecute(row), Is.True);
            Assert.That(vm.HasErrors, Is.False);
            Assert.That(vm.ErrorMessage, Is.Null);
        }
        finally { await vm.DeactivateAsync(); }
    }

    [Test]
    public async Task CloseRefusesDraftWithoutSaving()
    {
        var renameCalls = 0;
        var vm = CreateManager(new StubSampleFileService(["hit.wav"])
        { Rename = (_, _, _, _) => { renameCalls++; return Task.FromResult("kick.wav"); } });
        try
        {
            await vm.InitializeAsync(default); await vm.EditCommand.ExecuteAsync(vm.Entries.Single()); vm.DraftStem = "kick";
            Assert.That(await vm.PrepareToCloseAsync(), Is.False);
            Assert.That(renameCalls, Is.Zero);
            Assert.That(vm.ActiveEdit, Is.Not.Null);
            vm.CancelEditCommand.Execute(null);
            Assert.That(await vm.PrepareToCloseAsync(), Is.True);
        }
        finally { await vm.DeactivateAsync(); }
    }

    [Test]
    public async Task UnchangedDraftExitsEditingWithoutRename()
    {
        var vm = CreateManager(new StubSampleFileService(["hit.wav"])
        { Rename = (_, _, _, _) => throw new AssertionException("Unchanged filename must not rename") });
        try
        {
            await vm.InitializeAsync(default); var row = vm.Entries.Single(); await vm.EditCommand.ExecuteAsync(row);
            await vm.EditCommand.ExecuteAsync(row);
            Assert.That(vm.ActiveEdit, Is.Null); Assert.That(row.IsEditing, Is.False);
            Assert.That(vm.ErrorMessage, Is.Null);
        }
        finally { await vm.DeactivateAsync(); }
    }

    [Test]
    public async Task FailedRenameRetainsDraftAndAllowsRetry()
    {
        var files = new StubSampleFileService(["hit.wav"]) { Rename = (_, _, _, _) => Task.FromException<string>(new IOException("Denied")) };
        var vm = CreateManager(files);
        try
        {
            await vm.InitializeAsync(default); var row = vm.Entries.Single(); await vm.EditCommand.ExecuteAsync(row); vm.DraftStem = "kick";
            await vm.EditCommand.ExecuteAsync(row);
            Assert.That(vm.ActiveEdit, Is.SameAs(row)); Assert.That(vm.DraftStem, Is.EqualTo("kick"));
            Assert.That(vm.ErrorMessage, Is.EqualTo("Denied")); Assert.That(vm.EditCommand.CanExecute(row), Is.True);
            var pending = new TaskCompletionSource<string>(); files.Rename = (_, _, _, _) => pending.Task;
            files.List = (_, _) => Task.FromResult<IReadOnlyList<string>>(["kick.wav"]);
            var saving = vm.EditCommand.ExecuteAsync(row);
            try
            {
                Assert.That(vm.CommitRenameCommand.CanExecute(null), Is.False);
                Assert.That(vm.CancelEditCommand.CanExecute(null), Is.False);
                var closing = vm.PrepareToCloseAsync();
                Assert.That(closing.IsCompletedSuccessfully, Is.True);
                Assert.That(await closing, Is.False);
            }
            finally { pending.TrySetResult("kick.wav"); await saving; }
            Assert.That(vm.ActiveEdit, Is.Null); Assert.That(row.FileName, Is.EqualTo("kick.wav"));
        }
        finally { await vm.DeactivateAsync(); }
    }

    private static ManageSamplesPopupVM CreateManager(StubSampleFileService files) => new(new("project.json", null, Guid.NewGuid()), files, new(files),
        new StubSampleFileDialogService([]), new StubSampleRemovalDialogService(), new WeakReferenceMessenger(), new ImmediateUiScheduler());

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

