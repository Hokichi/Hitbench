using CommunityToolkit.Mvvm.Messaging;
using Onset.Core.Entities;
using Onset.Resources.DataModels;
using Onset.Resources.Messages;
using Onset.Services;
using Onset.ViewModels.Shell;
namespace Onset.Test.Presentation;

public class InstrumentalNoteHistoryIntegrationTests
{
    [Test]
    public async Task BatchCollisionAndStaleRevisionLeaveNotesAndHistoryUnchanged()
    {
        var track=new InstrumentalTrack{KeyFiles=new(){["C4"]="piano.wav"}};
        var messenger=new WeakReferenceMessenger();var dialogs=new StubInstrumentalTrackDialogService();
        var owner=new WorkstationPageVM(new StubWorkstationSourceService{Load=(_,_)=>Task.FromResult(new WorkstationSource("map","song",new ProjectData{Tracks=[track],Offsets=[0],BPMs=[120]}))},new MarkerPlaybackCoordinator(new StubWorkstationAudioService(),Task.Delay),new ImmediateUiScheduler(),messenger:messenger,instrumentDialogs:dialogs);
        InstrumentalTrackSnapshot? latest=null;messenger.Register<InstrumentalTrackChangedMessage>(this,(_,m)=>latest=m.Snapshot);
        dialogs.Arrangement=(data,_)=>
        {
            var ctx=data.Track.Context;messenger.Send(new PlaceInstrumentalNoteRequestedMessage(ctx,Guid.NewGuid(),"C4",125,4));
            var before=latest!;var note=before.Notes.Single();
            messenger.Send(new ReplaceInstrumentalNotesRequestedMessage(ctx,Guid.NewGuid(),before.Revision,[note,note with{Id=Guid.NewGuid()}],[note.Id]));
            Assert.That(track.Notes.Count,Is.EqualTo(1));Assert.That(latest!.Revision,Is.EqualTo(before.Revision));
            messenger.Send(new ReplaceInstrumentalNotesRequestedMessage(ctx,Guid.NewGuid(),before.Revision-1,[],[note.Id]));Assert.That(track.Notes.Count,Is.EqualTo(1));
            messenger.Send(new ReplayInstrumentalNoteHistoryRequestedMessage(ctx,Guid.NewGuid(),false));Assert.That(track.Notes,Is.Empty);
            messenger.Send(new ReplayInstrumentalNoteHistoryRequestedMessage(ctx,Guid.NewGuid(),true));Assert.That(latest!.Notes.Single(),Is.EqualTo(note));
            return Task.CompletedTask;
        };
        try{await owner.LoadAsync("project.json",default);owner.AcknowledgePresentationReady(owner.LoadGeneration);await owner.ViewArrangementCommand.ExecuteAsync(track);}
        finally{await owner.DeactivateAsync();messenger.UnregisterAll(this);}
    }
    [Test]
    public async Task UndoRetainsIdentityAndMappingAcrossReopen()
    {
        var track = new InstrumentalTrack { KeyFiles = new() { ["C4"] = "old.wav" } };
        var messenger = new WeakReferenceMessenger(); var dialogs = new StubInstrumentalTrackDialogService();
        var owner = new WorkstationPageVM(new StubWorkstationSourceService { Load = (_, _) => Task.FromResult(new WorkstationSource("map", "song", new ProjectData { Tracks = [track], Offsets = [0], BPMs = [120] })) }, new MarkerPlaybackCoordinator(new StubWorkstationAudioService(), Task.Delay), new ImmediateUiScheduler(), messenger: messenger, instrumentDialogs: dialogs);
        InstrumentalTrackSnapshot? latest = null;
        messenger.Register<InstrumentalTrackChangedMessage>(this, (_, m) => latest = m.Snapshot);
        Guid originalId = default;
        dialogs.Arrangement = (data, _) =>
        {
            var context = data.Track.Context;
            messenger.Send(new PlaceInstrumentalNoteRequestedMessage(context, Guid.NewGuid(), "C4", 125, 4));
            originalId = latest!.Notes.Single().Id;
            messenger.Send(new RemoveInstrumentalNoteRequestedMessage(context, Guid.NewGuid(), originalId));
            Assert.That(latest.CanUndo, Is.True);
            return Task.CompletedTask;
        };
        try
        {
            await owner.LoadAsync("project.json", default); owner.AcknowledgePresentationReady(owner.LoadGeneration);
            await owner.ViewArrangementCommand.ExecuteAsync(track);
            dialogs.Arrangement = (data, _) =>
            {
                Assert.That(data.Track.CanUndo, Is.True);
                messenger.Send(new SetInstrumentalKeyMappingRequestedMessage(data.Track.Context, Guid.NewGuid(), "C4", "new.wav"));
                messenger.Send(new ReplayInstrumentalNoteHistoryRequestedMessage(data.Track.Context, Guid.NewGuid(), false));
                Assert.That(latest!.Notes.Single().Id, Is.EqualTo(originalId));
                Assert.That(latest.KeyFiles["C4"], Is.EqualTo("new.wav"));
                Assert.That(latest.CanRedo, Is.True);
                Assert.That(owner.CanUndo, Is.False);
                return Task.CompletedTask;
            };
            await owner.ViewArrangementCommand.ExecuteAsync(track);
        }
        finally { await owner.DeactivateAsync(); messenger.UnregisterAll(this); }
    }
}
