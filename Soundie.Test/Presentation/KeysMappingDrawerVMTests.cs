using CommunityToolkit.Mvvm.Messaging;
using Soundie.Core.Entities;
using Soundie.Services;
using Soundie.ViewModels.Shell;
using Soundie.ViewModels.Popups;
namespace Soundie.Test.Presentation;

public class KeysMappingDrawerVMTests
{
    [Test]
    public async Task SaveOnlyNoticesPreserveDirtyDraftsAndRejectOlderStatus()
    {
        var context = new Soundie.Resources.DataModels.InstrumentalTrackContext("project.json", null, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        var snapshot = new Soundie.Resources.DataModels.InstrumentalTrackSnapshot(context, "Piano", 100, new Dictionary<string, string> { ["C1"] = "old.wav" }, [], ["old.wav"], 0);
        var messenger = new WeakReferenceMessenger();
        var vm = new KeysMappingDrawerVM(snapshot, messenger, new ImmediateUiScheduler(), new StubSampleFileService([]), new StubSampleFileDialogService((string?)null));
        try
        {
            vm.Rows[0].DraftFileName = "new.wav";
            messenger.Send(new Soundie.Resources.Messages.InstrumentalSaveStateChangedMessage(context, 2, false, "Disk full"));
            messenger.Send(new Soundie.Resources.Messages.InstrumentalSaveStateChangedMessage(context, 1, false, null));
            Assert.That(vm.Rows[0].DraftFileName, Is.EqualTo("new.wav")); Assert.That(vm.Rows[0].FileName, Is.EqualTo("old.wav")); Assert.That(vm.SaveError, Is.EqualTo("Disk full"));
        }
        finally { await vm.DeactivateAsync(); }
    }
    [Test]
    public async Task ConcurrentTeardownWaitsForCancelledImport()
    {
        var context = new Soundie.Resources.DataModels.InstrumentalTrackContext("project.json", null, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        var snapshot = new Soundie.Resources.DataModels.InstrumentalTrackSnapshot(context, "Piano", 100, new Dictionary<string, string>(), [], [], 0);
        var release = new TaskCompletionSource<IReadOnlyList<string>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var picker = new StubSampleFileDialogService((string?)null) { Selection = _ => release.Task };
        var messenger = new WeakReferenceMessenger();
        var requests = 0;
        messenger.Register<Soundie.Resources.Messages.SetInstrumentalKeyMappingRequestedMessage>(this, (_, _) => requests++);
        var vm = new KeysMappingDrawerVM(snapshot, messenger, new ImmediateUiScheduler(), new StubSampleFileService([]), picker);
        var import = vm.OpenKeyAsync("C4", default);
        var first = vm.DeactivateAsync();
        var second = vm.DeactivateAsync();
        try { Assert.That(first.IsCompleted, Is.False); Assert.That(second.IsCompleted, Is.False); }
        finally { release.SetResult(["late.wav"]); await Task.WhenAll(first, second, import); messenger.UnregisterAll(this); }
        Assert.That(requests, Is.Zero);
    }
    [TestCase(false)]
    [TestCase(true)]
    public async Task ValidMappingAutosavesAndInvalidDraftPreservesOldFile(bool folderScanFails)
    {
        var track = new InstrumentalTrack { KeyFiles = new() { ["C1"] = "old.wav" } };
        var project = new ProjectData { Tracks = [track], Offsets = [0], BPMs = [120] };
        var messenger = new WeakReferenceMessenger(); var dialogs = new StubInstrumentalTrackDialogService();
        var files = new StubSampleFileService(["old.wav"]);
        var audio = new StubWorkstationAudioService();
        var owner = new WorkstationPageVM(new StubWorkstationSourceService { Load = (_, _) => Task.FromResult(new WorkstationSource("map", "song", project)) },
         new MarkerPlaybackCoordinator(audio, Task.Delay), new ImmediateUiScheduler(),
         sampleFiles: files, messenger: messenger, instrumentDialogs: dialogs);
        dialogs.Mapping = async (snapshot, token) =>
        {
            files.List = (_, _) => folderScanFails ? Task.FromException<IReadOnlyList<string>>(new IOException("scan failed")) : Task.FromResult<IReadOnlyList<string>>(["old.wav", "Instrumental/new.wav"]);
            var vm = new KeysMappingDrawerVM(snapshot, messenger, new ImmediateUiScheduler(), files, new StubSampleFileDialogService("new.wav"));
            try
            {
                Assert.That(vm.Rows.Count, Is.EqualTo(108));
                Assert.That(await vm.CommitMappingAsync("C1", "../bad.wav"), Is.False);
                Assert.That(track.KeyFiles["C1"], Is.EqualTo("old.wav"));
                await vm.OpenKeyAsync("C1", token); Assert.That(track.KeyFiles["C1"], Is.EqualTo("Instrumental/new.wav"));
                Assert.That(owner.SampleFiles, Is.EqualTo(new[] { "old.wav" }));
                Assert.That(vm.Rows[0].IsMissing, Is.False);
                if (folderScanFails) Assert.That(vm.ErrorMessage, Does.Contain("could not refresh"));
                vm.Rows[0].DraftFileName = ""; await vm.PrepareCloseAsync(); Assert.That(track.KeyFiles.ContainsKey("C1"), Is.False);
            }
            finally { await vm.DeactivateAsync(); }
        };
        try { await owner.LoadAsync("project.json", default); owner.AcknowledgePresentationReady(owner.LoadGeneration); await owner.KeysMappingCommand.ExecuteAsync(track); }
        finally { await owner.DeactivateAsync(); }
    }
}
