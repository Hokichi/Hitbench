using CommunityToolkit.Mvvm.Messaging;
using osuHitsounder.Core.Entities;
using osuHitsounder.Resources.Messages;
using osuHitsounder.Resources.DataModels;
using osuHitsounder.Services;
using osuHitsounder.Services.ProjectFiles;
using osuHitsounder.ViewModels.Shell;

namespace osuHitsounder.Test.Presentation;

public class WorkstationInstrumentalEditingTests
{
    [Test] public async Task PlacementSnapsSelectsAndRejectsStaleSessions()
    {
        var track=new InstrumentalTrack();
        var project=new ProjectData { Tracks=[track],Offsets=[0],BPMs=[137] };
        var messenger=new WeakReferenceMessenger(); var dialogs=new StubInstrumentalTrackDialogService();
        var saves=new DelayedProjectFileService(); saves.Release();
        var owner=new WorkstationPageVM(new StubWorkstationSourceService { Load=(_,_)=>Task.FromResult(new WorkstationSource("map.osu","song.wav",project)) },
            new MarkerPlaybackCoordinator(new StubWorkstationAudioService(),Task.Delay),new ImmediateUiScheduler(),
            projectFiles:saves,messenger:messenger,instrumentDialogs:dialogs);
        InstrumentalTrackContext? old=null; InstrumentalEditResultMessage? result=null;
        messenger.Register<InstrumentalEditResultMessage>(this,(_,m)=>result=m);
        dialogs.Arrangement=(ctx,_)=>
        {
            old=ctx.Track.Context;
            var id=Guid.NewGuid(); messenger.Send(new PlaceInstrumentalNoteRequestedMessage(old,id,"C4",146.1,3));
            Assert.That(track.Notes.Single().Timestamp,Is.EqualTo(146));
            Assert.That(result!.RequestId,Is.EqualTo(id));
            var selected=result.SelectedNoteId;
            messenger.Send(new PlaceInstrumentalNoteRequestedMessage(old,Guid.NewGuid(),"C4",146.1,3));
            Assert.That(result.Changed,Is.False); Assert.That(result.SelectedNoteId,Is.EqualTo(selected));
            Assert.That(track.Notes,Has.Count.EqualTo(1));
            messenger.Send(new MoveInstrumentalNoteRequestedMessage(old,Guid.NewGuid(),selected!.Value,"B9",300,3));
            Assert.That(track.Notes.Single().Key,Is.EqualTo("B9"));
            return Task.CompletedTask;
        };
        try
        {
            await owner.LoadAsync("project.json",default); owner.AcknowledgePresentationReady(owner.LoadGeneration);
            await owner.ViewArrangementCommand.ExecuteAsync(track); await owner.FlushSavesAsync();
            messenger.Send(new RemoveInstrumentalNoteRequestedMessage(old!,Guid.NewGuid(),result!.SelectedNoteId!.Value));
            Assert.That(track.Notes,Has.Count.EqualTo(1));
            Assert.That(((InstrumentalTrack)ProjectDataSerializer.Deserialize(saves.LatestJson!).Tracks[0]).Notes.Single().Key,Is.EqualTo("B9"));
        }
        finally { await owner.DeactivateAsync(); messenger.UnregisterAll(this); }
    }
    [Test] public async Task InstrumentCreationNeedsNoFiles()
    {
        var owner=new WorkstationPageVM(new StubWorkstationSourceService(),new MarkerPlaybackCoordinator(new StubWorkstationAudioService(),Task.Delay),new ImmediateUiScheduler());
        try { await owner.LoadAsync("project.json",default); owner.AcknowledgePresentationReady(owner.LoadGeneration); owner.AddPianoTrackCommand.Execute(null); Assert.That(owner.Tracks.OfType<InstrumentalTrack>().Count(),Is.EqualTo(1)); }
        finally { await owner.DeactivateAsync(); }
    }
}
