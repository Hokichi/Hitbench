using CommunityToolkit.Mvvm.Messaging;
using Soundie.Core.Entities;
using Soundie.Resources.Messages;
using Soundie.Services;
using Soundie.ViewModels.Shell;
namespace Soundie.Test.Presentation;
public class KeysMappingClearTests
{
    [TestCase(false)][TestCase(true)]
    public async Task ClearHonorsRevisionAndPreservesNotes(bool stale)
    {
        var track = new InstrumentalTrack { KeyFiles = new() { ["C5"] = "piano.wav" }, Notes = [new() { Key = "C5", Timestamp = 125 }] };
        var messenger = new WeakReferenceMessenger();
        var dialogs = new StubInstrumentalTrackDialogService();
        var owner = new WorkstationPageVM(new StubWorkstationSourceService { Load = (_, _) => Task.FromResult(new WorkstationSource("map", "song", new ProjectData { Tracks = [track], Offsets = [0], BPMs = [120] })) }, new MarkerPlaybackCoordinator(new StubWorkstationAudioService(), Task.Delay), new ImmediateUiScheduler(), messenger: messenger, instrumentDialogs: dialogs);
        dialogs.Mapping = (snapshot, _) => {
            messenger.Send(new OHsClearInstrumentalKeyMappingsRequestedMessage(snapshot.Context, Guid.NewGuid(), snapshot.Revision - (stale ? 1 : 0)));
            Assert.That(track.KeyFiles.Count, Is.EqualTo(stale ? 1 : 0));
            Assert.That(track.Notes.Count, Is.EqualTo(1));
            return Task.CompletedTask;
        };
        try { await owner.LoadAsync("project.json", default); owner.AcknowledgePresentationReady(owner.LoadGeneration); await owner.KeysMappingCommand.ExecuteAsync(track); }
        finally { await owner.DeactivateAsync(); }
    }
    [TestCase(false)][TestCase(true)]
    public async Task ConfirmationCancelPreservesDraftAndAcceptClearsWithoutDeletingNotes(bool confirm)
    {
        var track = new InstrumentalTrack { KeyFiles = new() { ["C5"] = "piano.wav" }, Notes = [new() { Key = "C5", Timestamp = 125 }] };
        var messenger = new WeakReferenceMessenger(); var dialogs = new StubInstrumentalTrackDialogService();
        var owner = new WorkstationPageVM(new StubWorkstationSourceService { Load = (_, _) => Task.FromResult(new WorkstationSource("map","song",new ProjectData { Tracks = [track],Offsets=[0],BPMs=[120] })) },new MarkerPlaybackCoordinator(new StubWorkstationAudioService(),Task.Delay),new ImmediateUiScheduler(),messenger:messenger,instrumentDialogs:dialogs);
        dialogs.Mapping = async (snapshot,_) => {
            var vm = new Soundie.ViewModels.Popups.KeysMappingDrawerVM(snapshot,messenger,new ImmediateUiScheduler(),new StubSampleFileService([]),new StubSampleFileDialogService((string?)null),confirmClearMappings:(_,_)=>Task.FromResult(confirm));
            try {
                await vm.SelectKeyAsync("C5"); vm.SelectedEntry!.DraftFileName = "../invalid.wav";
                await vm.ClearAllMappingsCommand.ExecuteAsync(null);
                Assert.That(track.KeyFiles.Count, Is.EqualTo(confirm ? 0 : 1));
                Assert.That(vm.SelectedEntry.DraftFileName, Is.EqualTo(confirm ? "" : "../invalid.wav"));
                Assert.That(track.Notes.Count, Is.EqualTo(1));
            } finally { await vm.DeactivateAsync(); }
        };
        try { await owner.LoadAsync("project.json",default);owner.AcknowledgePresentationReady(owner.LoadGeneration);await owner.KeysMappingCommand.ExecuteAsync(track); }
        finally { await owner.DeactivateAsync(); }
    }
}
