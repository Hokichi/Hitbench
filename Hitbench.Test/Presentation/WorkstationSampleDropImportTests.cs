using CommunityToolkit.Mvvm.Messaging;
using Hitbench.Services;
using Hitbench.Services.Ui;
using Hitbench.Resources.Messages;
using Hitbench.ViewModels.Shell;
namespace Hitbench.Test.Presentation;

public sealed class WorkstationSampleDropImportTests
{
    [TestCase("cancel")]
    [TestCase("present")]
    [TestCase("copy")]
    [TestCase("success")]
    public async Task EveryTerminalImportReleasesManagerReservation(string outcome)
    {
        var path = Path.ChangeExtension(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")), ".wav");
        await File.WriteAllTextAsync(path, "");
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var messenger = new WeakReferenceMessenger();
        var finished = new List<OHsSampleDropImportFinishedMessage>();
        messenger.Register<OHsSampleDropImportFinishedMessage>(finished, (_, message) => finished.Add(message));
        var files = new StubSampleFileService(new[] { "old.wav" }) { Import = (_, _, _) => outcome == "copy" ? Task.FromException<string>(new IOException("copy failed")) : Task.FromResult("new.wav") };
        Hitbench.ViewModels.Popups.ManageSamplesPopupVM? manager = null;
        var management = new StubSampleManagementDialogService { Present = async (context, _) =>
        {
            manager = new(context, files, new(files), new StubSampleFileDialogService(Array.Empty<string>()), new StubSampleRemovalDialogService(), messenger, new ImmediateUiScheduler(), new StubSamplePreviewService());
            await manager.InitializeAsync(default); entered.TrySetResult(); await release.Task;
        } };
        var picker = new StubOHsSampleDropImportDialogService { Select = (_, _) => outcome == "present" ? Task.FromException<IReadOnlyList<string>>(new IOException("presentation failed")) : Task.FromResult<IReadOnlyList<string>>(outcome == "cancel" ? Array.Empty<string>() : new[] { path }) };
        var vm = new WorkstationPageVM(new StubWorkstationSourceService(), new MarkerPlaybackCoordinator(new StubWorkstationAudioService(), MarkerPlaybackCoordinator.DelayAsync), new ImmediateUiScheduler(), files, new StubSampleFileDialogService(Array.Empty<string>()), messenger: messenger, sampleDropDialog: picker, sampleManagementDialog: management);
        Task? managing = null;
        try
        {
            await vm.LoadAsync("project.json", default); vm.AcknowledgePresentationReady(vm.LoadGeneration); vm.IsAdditionMode = true;
            managing = vm.ManageSamplesCommand.ExecuteAsync(null); await entered.Task;
            await vm.ImportDroppedSamplesAsync(new[] { path });
            Assert.That(finished, Has.Count.EqualTo(1));
            Assert.That(manager!.CanClose, Is.True);
            Assert.That(vm.CanImportDroppedSamples, Is.True);
        }
        finally { release.TrySetResult(); if (managing is not null) await managing; if (manager is not null) await manager.DeactivateAsync(); await vm.DeactivateAsync(); File.Delete(path); }
    }
    [TestCase(false)]
    [TestCase(true)]
    public async Task LatePresenterCannotRefreshReplacementProject(bool fail)
    {
        var path = Path.ChangeExtension(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")), ".wav");
        await File.WriteAllTextAsync(path, "");
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var gate = new TaskCompletionSource<IReadOnlyList<string>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var copied = new List<string>();
        var picker = new StubOHsSampleDropImportDialogService { Select = (_, _) => { entered.TrySetResult(); return gate.Task; } };
        var files = new StubSampleFileService(new[] { "old.wav" }) { Import = (_, source, _) => { copied.Add(source); return Task.FromResult("new.wav"); } };
        var vm = new WorkstationPageVM(new StubWorkstationSourceService(), new MarkerPlaybackCoordinator(new StubWorkstationAudioService(), MarkerPlaybackCoordinator.DelayAsync), new ImmediateUiScheduler(), files, new StubSampleFileDialogService(Array.Empty<string>()), sampleDropDialog: picker);
        try
        {
            await vm.LoadAsync("old.json", default); vm.AcknowledgePresentationReady(vm.LoadGeneration);
            var import = vm.ImportDroppedSamplesAsync(new[] { path }); await entered.Task;
            var replacement = vm.LoadAsync("new.json", default);
            if (fail) gate.TrySetException(new IOException("late presentation failed")); else gate.TrySetResult(new[] { path });
            Assert.That(await import, Is.Empty); await replacement; vm.AcknowledgePresentationReady(vm.LoadGeneration);
            Assert.That(copied, Is.Empty);
            Assert.That(vm.CanImportDroppedSamples, Is.True);
        }
        finally { gate.TrySetResult(Array.Empty<string>()); await vm.DeactivateAsync(); File.Delete(path); }
    }
    [TestCase(false)]
    [TestCase(true)]
    public async Task CheckedFilesRefreshWithinOneProgressLifetime(bool failOne)
    {
        var folder = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")); Directory.CreateDirectory(folder);
        var a = Path.Combine(folder, "a.wav"); var b = Path.Combine(folder, "b.ogg"); await File.WriteAllTextAsync(a, ""); await File.WriteAllTextAsync(b, "");
        var names = new List<string> { "old.wav" }; var attempted = new List<string>(); var gate = new TaskCompletionSource();
        var files = new StubSampleFileService(names) { Import = (_, path, _) => { attempted.Add(path); if (failOne && path == a) throw new IOException("missing source"); names.Add(Path.GetFileName(path)); return Task.FromResult(Path.GetFileName(path)); } };
        var picker = new StubOHsSampleDropImportDialogService(); var messenger = new WeakReferenceMessenger(); var messages = new List<OHsAudioBusyChangedMessage>(); messenger.Register<OHsAudioBusyChangedMessage>(messages, (_, m) => messages.Add(m));
        var progress = new OHsAudioOperationProgress(messenger, new ImmediateUiScheduler(), (_, _) => Task.CompletedTask);
        var audio = new StubWorkstationAudioService();
        var vm = new WorkstationPageVM(new StubWorkstationSourceService(), new MarkerPlaybackCoordinator(audio, MarkerPlaybackCoordinator.DelayAsync), new ImmediateUiScheduler(), files, new StubSampleFileDialogService(Array.Empty<string>()), messenger: messenger, sampleDropDialog: picker, audioOperationProgress: progress);
        try
        {
            await vm.LoadAsync("project.json", default); vm.AcknowledgePresentationReady(vm.LoadGeneration); vm.AddSampleTrackCommand.Execute(null);
            audio.PrepareSamplesAsync = _ => progress.RunAsync("nested", _ => gate.Task, default);
            var import = vm.ImportDroppedSamplesAsync(new[] { a, b });
            await StubWorkstationAudioService.WaitUntilAsync(() => messages.Count > 0);
            Assert.That(import.IsCompleted, Is.False); Assert.That(await vm.ImportDroppedSamplesAsync(new[] { a }), Is.Empty);
            gate.SetResult(); var result = await import;
            Assert.That(attempted, Is.EqualTo(new[] { a, b })); Assert.That(result, Does.Contain("b.ogg"));
            Assert.That(vm.SampleFiles, Does.Contain("b.ogg")); Assert.That(vm.Tracks.Single().SelectedSampleFileName, Is.EqualTo("old.wav"));
            Assert.That(messages.Select(m => m.IsBusy), Is.EqualTo(new[] { true, false })); Assert.That(messages.Select(m => m.OperationId).Distinct().Count(), Is.EqualTo(1));
            if (failOne) Assert.That(vm.ErrorMessage, Does.Contain("missing source"));
        }
        finally { gate.TrySetResult(); await vm.DeactivateAsync(); Directory.Delete(folder, true); }
    }
    [Test]
    public async Task CancellationDuringPopupSettlesAndDisablesNoReplacementProject()
    {
        var source = Path.GetTempFileName(); var path = Path.ChangeExtension(source, ".wav"); File.Move(source, path);
        var entered = new TaskCompletionSource(); var picker = new StubOHsSampleDropImportDialogService { Select = async (_, token) => { entered.SetResult(); await Task.Delay(Timeout.Infinite, token); return Array.Empty<string>(); } };
        var files = new StubSampleFileService(new[] { "old.wav" });
        var vm = new WorkstationPageVM(new StubWorkstationSourceService(), new MarkerPlaybackCoordinator(new StubWorkstationAudioService(), MarkerPlaybackCoordinator.DelayAsync), new ImmediateUiScheduler(), files, new StubSampleFileDialogService(Array.Empty<string>()), sampleDropDialog: picker);
        try { await vm.LoadAsync("old.json", default); vm.AcknowledgePresentationReady(vm.LoadGeneration); var import = vm.ImportDroppedSamplesAsync(new[] { path }); await entered.Task; await vm.LoadAsync("new.json", default); vm.AcknowledgePresentationReady(vm.LoadGeneration); Assert.That(await import, Is.Empty); Assert.That(vm.CanImportDroppedSamples, Is.True); }
        finally { await vm.DeactivateAsync(); File.Delete(path); }
    }
 [Test] public async Task CancelAndCheckedSubsetNeverImportOtherFiles()
 {
  var folder=Path.Combine(Path.GetTempPath(),Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);var a=Path.Combine(folder,"a.wav");var b=Path.Combine(folder,"b.ogg");await File.WriteAllTextAsync(a,"");await File.WriteAllTextAsync(b,"");
  var attempted=new List<string>();var files=new StubSampleFileService(new[]{"old.wav"}){Import=(_,path,_)=>{attempted.Add(path);return Task.FromResult(Path.GetFileName(path));}};
  var picker=new StubOHsSampleDropImportDialogService {Select=(_,_)=>Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>())};
  var vm=new WorkstationPageVM(new StubWorkstationSourceService(),new MarkerPlaybackCoordinator(new StubWorkstationAudioService(),MarkerPlaybackCoordinator.DelayAsync),new ImmediateUiScheduler(),files,new StubSampleFileDialogService(Array.Empty<string>()),sampleDropDialog:picker);
  try {await vm.LoadAsync("project.json",default);vm.AcknowledgePresentationReady(vm.LoadGeneration);Assert.That(await vm.ImportDroppedSamplesAsync(new[]{a,b}),Is.Empty);Assert.That(attempted,Is.Empty);picker.Select=(_,_)=>Task.FromResult<IReadOnlyList<string>>(new[]{b,"C:/unrequested.mp3"});await vm.ImportDroppedSamplesAsync(new[]{a,b});Assert.That(attempted,Is.EqualTo(new[]{b}));}
  finally {await vm.DeactivateAsync();Directory.Delete(folder,true);}
 }
}
