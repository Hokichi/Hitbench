using CommunityToolkit.Mvvm.Messaging;
using Soundie.Core.Entities;
using Soundie.Resources.Messages;
using Soundie.Resources.DataModels;
using Soundie.Services;
using Soundie.Services.ProjectFiles;
using Soundie.ViewModels.Shell;

namespace Soundie.Test.Presentation;

public class WorkstationInstrumentalEditingTests
{
    [Test]
    public async Task ArrangementRequiresAMappingAndUpdatesAfterMappingChanges()
    {
        var track = new InstrumentalTrack();
        var messenger = new WeakReferenceMessenger();
        var dialogs = new StubInstrumentalTrackDialogService();
        var opens = 0;
        dialogs.Arrangement = (_, _) => { opens++; return Task.CompletedTask; };
        var owner = new WorkstationPageVM(new StubWorkstationSourceService { Load = (_, _) => Task.FromResult(new WorkstationSource("map", "song", new ProjectData { Tracks = [track], Offsets = [0], BPMs = [120] })) },
            new MarkerPlaybackCoordinator(new StubWorkstationAudioService(), Task.Delay), new ImmediateUiScheduler(), messenger: messenger, instrumentDialogs: dialogs);
        try
        {
            await owner.LoadAsync("project.json", default); owner.AcknowledgePresentationReady(owner.LoadGeneration);
            Assert.That(owner.ViewArrangementCommand.CanExecute(track), Is.False);
            Assert.That(owner.KeysMappingCommand.CanExecute(track), Is.True);
            await owner.ViewArrangementCommand.ExecuteAsync(track);
            Assert.That(opens, Is.Zero);
            dialogs.Mapping = (snapshot, _) =>
            {
                messenger.Send(new SetInstrumentalKeyMappingRequestedMessage(snapshot.Context, Guid.NewGuid(), "C4", "missing.wav"));
                return Task.CompletedTask;
            };
            await owner.KeysMappingCommand.ExecuteAsync(track);
            Assert.That(owner.ViewArrangementCommand.CanExecute(track), Is.True);
            await owner.ViewArrangementCommand.ExecuteAsync(track);
            Assert.That(opens, Is.EqualTo(1));
            dialogs.Mapping = (snapshot, _) =>
            {
                messenger.Send(new SetInstrumentalKeyMappingRequestedMessage(snapshot.Context, Guid.NewGuid(), "C4", null));
                return Task.CompletedTask;
            };
            await owner.KeysMappingCommand.ExecuteAsync(track);
            Assert.That(owner.ViewArrangementCommand.CanExecute(track), Is.False);
            await owner.ViewArrangementCommand.ExecuteAsync(track);
            Assert.That(opens, Is.EqualTo(1));
        }
        finally { await owner.DeactivateAsync(); }
    }
    [Test]
    public async Task SettlementCancelsEditorAndDrainsLatestSaveWithoutSelfAwait()
    {
        var track = new InstrumentalTrack { KeyFiles = new() { ["C4"] = "piano.wav" } };
        var messenger = new WeakReferenceMessenger(); var dialogs = new StubInstrumentalTrackDialogService(); var saves = new DelayedProjectFileService();
        var audio = new StubWorkstationAudioService();
        var entered = new TaskCompletionSource<InstrumentalTrackContext>();
        var states = new List<InstrumentalSaveStateChangedMessage>();
        messenger.Register<InstrumentalSaveStateChangedMessage>(this, (_, m) => states.Add(m));
        dialogs.Arrangement = async (data, token) =>
        {
            messenger.Send(new InstrumentalEditorReadyMessage(data.Track.Context));
            messenger.Send(new PlaceInstrumentalNoteRequestedMessage(data.Track.Context, Guid.NewGuid(), "C4", 125, 4));
            messenger.Send(new PlaceInstrumentalNoteRequestedMessage(data.Track.Context, Guid.NewGuid(), "E4", 125, 4));
            entered.SetResult(data.Track.Context);
            await Task.Delay(Timeout.Infinite, token);
        };
        var owner = new WorkstationPageVM(new StubWorkstationSourceService { Load = (_, _) => Task.FromResult(new WorkstationSource("map", "song", new ProjectData { Tracks = [track], Offsets = [0], BPMs = [120] })) },
            new MarkerPlaybackCoordinator(audio, Task.Delay), new ImmediateUiScheduler(), projectFiles: saves, messenger: messenger, instrumentDialogs: dialogs);
        try
        {
            await owner.LoadAsync("project.json", default); owner.AcknowledgePresentationReady(owner.LoadGeneration);
            var editor = owner.ViewArrangementCommand.ExecuteAsync(track); var context = await entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
            Assert.That(owner.KeysMappingCommand.CanExecute(track), Is.True);
            Assert.That(owner.PlayPauseCommand.CanExecute(null), Is.True);
            await owner.PlayPauseCommand.ExecuteAsync(null);
            await owner.SeekCommand.ExecuteAsync(TimeSpan.FromSeconds(2));
            Assert.That(audio.Snapshot.IsPlaying, Is.True);
            Assert.That(audio.Snapshot.Position, Is.EqualTo(TimeSpan.FromSeconds(2)));
            Assert.That(states.Last().IsSaving, Is.True);
            var settlement = owner.SettleInstrumentalEditorAsync(); Assert.That(settlement.IsCompleted, Is.False);
            saves.Release(); await Task.WhenAll(editor, settlement).WaitAsync(TimeSpan.FromSeconds(3));
            Assert.That(states.Last().IsSaving, Is.False); Assert.That(states.Last().Error, Is.Null);
            Assert.That(saves.Snapshots, Has.Count.EqualTo(2));
            Assert.That(((InstrumentalTrack)ProjectDataSerializer.Deserialize(saves.LatestJson!).Tracks[0]).Notes, Has.Count.EqualTo(2));
            messenger.Send(new PlaceInstrumentalNoteRequestedMessage(context, Guid.NewGuid(), "G4", 125, 4));
            Assert.That(track.Notes, Has.Count.EqualTo(2));
        }
        finally { saves.Release(); await owner.DeactivateAsync(); messenger.UnregisterAll(this); }
    }

    [Test]
    public async Task FailedWriteStaysVisibleUntilRetrySucceeds()
    {
        var track = new InstrumentalTrack(); var saves = new FailOnceProjectFileService(); var messenger = new WeakReferenceMessenger();
        var dialogs = new StubInstrumentalTrackDialogService(); InstrumentalSaveStateChangedMessage? state = null;
        messenger.Register<InstrumentalSaveStateChangedMessage>(this, (_, m) => state = m);
        var owner = new WorkstationPageVM(new StubWorkstationSourceService { Load = (_, _) => Task.FromResult(new WorkstationSource("map", "song", new ProjectData { Tracks = [track], Offsets = [0], BPMs = [120] })) },
            new MarkerPlaybackCoordinator(new StubWorkstationAudioService(), Task.Delay), new ImmediateUiScheduler(), projectFiles: saves, messenger: messenger, instrumentDialogs: dialogs);
        dialogs.Mapping = async (data, _) =>
        {
            messenger.Send(new SetInstrumentalKeyMappingRequestedMessage(data.Context, Guid.NewGuid(), "C4", "missing.wav"));
            Assert.That(state!.Error, Does.Contain("Disk full")); Assert.That(state.IsSaving, Is.False);
            owner.RetrySaveCommand.Execute(null);
            await StubWorkstationAudioService.WaitUntilAsync(() => state.Error is null);
            Assert.That(state.IsSaving, Is.False);
        };
        try { await owner.LoadAsync("project.json", default); owner.AcknowledgePresentationReady(owner.LoadGeneration); await owner.KeysMappingCommand.ExecuteAsync(track); await owner.FlushSavesAsync(); Assert.That(saves.SavedJson, Is.Not.Null); }
        finally { await owner.DeactivateAsync(); messenger.UnregisterAll(this); }
    }

    [Test]
    public async Task PersistedNotesAtSongEndFailWithoutWriting()
    {
        var saves = new DelayedProjectFileService(); saves.Release();
        var project = new ProjectData { Tracks = [new InstrumentalTrack { Notes = [new() { Key = "C4", Timestamp = 10000 }] }], Offsets = [0], BPMs = [120] };
        var owner = new WorkstationPageVM(new StubWorkstationSourceService { Load = (_, _) => Task.FromResult(new WorkstationSource("map", "song", project)) },
            new MarkerPlaybackCoordinator(new StubWorkstationAudioService(), Task.Delay), new ImmediateUiScheduler(), projectFiles: saves);
        try { await owner.LoadAsync("project.json", default); Assert.That(owner.ErrorMessage, Does.Contain("beyond the song")); Assert.That(owner.CanAddInstrumentalTrack, Is.False); Assert.That(saves.Writes, Is.Zero); }
        finally { await owner.DeactivateAsync(); }
    }
    [Test]
    public async Task PlacementSnapsSelectsAndRejectsStaleSessions()
    {
        var track = new InstrumentalTrack { KeyFiles = new() { ["C4"] = "piano.wav" } };
        var project = new ProjectData { Tracks = [track], Offsets = [0], BPMs = [137] };
        var messenger = new WeakReferenceMessenger(); var dialogs = new StubInstrumentalTrackDialogService();
        var saves = new DelayedProjectFileService(); saves.Release();
        var owner = new WorkstationPageVM(new StubWorkstationSourceService { Load = (_, _) => Task.FromResult(new WorkstationSource("map.osu", "song.wav", project)) },
            new MarkerPlaybackCoordinator(new StubWorkstationAudioService(), Task.Delay), new ImmediateUiScheduler(),
            projectFiles: saves, messenger: messenger, instrumentDialogs: dialogs);
        InstrumentalTrackContext? old = null; InstrumentalEditResultMessage? result = null;
        messenger.Register<InstrumentalEditResultMessage>(this, (_, m) => result = m);
        dialogs.Arrangement = (ctx, _) =>
        {
            old = ctx.Track.Context;
            var id = Guid.NewGuid(); messenger.Send(new PlaceInstrumentalNoteRequestedMessage(old, id, "C4", 146.1, 3));
            Assert.That(track.Notes.Single().Timestamp, Is.EqualTo(146));
            Assert.That(result!.RequestId, Is.EqualTo(id));
            var selected = result.SelectedNoteId;
            messenger.Send(new PlaceInstrumentalNoteRequestedMessage(old, Guid.NewGuid(), "C4", 146.1, 3));
            Assert.That(result.Changed, Is.False); Assert.That(result.SelectedNoteId, Is.EqualTo(selected));
            Assert.That(track.Notes, Has.Count.EqualTo(1));
            messenger.Send(new MoveInstrumentalNoteRequestedMessage(old, Guid.NewGuid(), selected!.Value, "B9", 300, 3));
            Assert.That(track.Notes.Single().Key, Is.EqualTo("B9"));
            return Task.CompletedTask;
        };
        try
        {
            await owner.LoadAsync("project.json", default); owner.AcknowledgePresentationReady(owner.LoadGeneration);
            await owner.ViewArrangementCommand.ExecuteAsync(track); await owner.FlushSavesAsync();
            messenger.Send(new RemoveInstrumentalNoteRequestedMessage(old!, Guid.NewGuid(), result!.SelectedNoteId!.Value));
            Assert.That(track.Notes, Has.Count.EqualTo(1));
            Assert.That(((InstrumentalTrack)ProjectDataSerializer.Deserialize(saves.LatestJson!).Tracks[0]).Notes.Single().Key, Is.EqualTo("B9"));
        }
        finally { await owner.DeactivateAsync(); messenger.UnregisterAll(this); }
    }
    [Test]
    public async Task InstrumentCreationNeedsNoFiles()
    {
        var owner = new WorkstationPageVM(new StubWorkstationSourceService(), new MarkerPlaybackCoordinator(new StubWorkstationAudioService(), Task.Delay), new ImmediateUiScheduler());
        try { await owner.LoadAsync("project.json", default); owner.AcknowledgePresentationReady(owner.LoadGeneration); owner.AddPianoTrackCommand.Execute(null); Assert.That(owner.Tracks.OfType<InstrumentalTrack>().Count(), Is.EqualTo(1)); }
        finally { await owner.DeactivateAsync(); }
    }
}
