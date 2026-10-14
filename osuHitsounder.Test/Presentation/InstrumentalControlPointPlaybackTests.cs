using CommunityToolkit.Mvvm.Messaging;
using osuHitsounder.Core.Entities;
using osuHitsounder.Resources.DataModels;
using osuHitsounder.Services;
using osuHitsounder.ViewModels.Popups;
using osuHitsounder.ViewModels.Shell;

namespace osuHitsounder.Test.Presentation;

public class InstrumentalControlPointPlaybackTests
{
    [TestCase(true)][TestCase(false)]
    public async Task ArrangementCopiesProjectTimelineAndKeepsMappedFiles(bool hasPoints)
    {
        var ctx=new InstrumentalTrackContext("project.json",null,Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid());
        var snapshot=new InstrumentalTrackSnapshot(ctx,"Piano",100,new Dictionary<string,string>{{"C4","piano.wav"}},[new(Guid.NewGuid(),"C4",1500,100)],null,0);
        var points=new List<ControlPointVolumeSnapshot>(); if(hasPoints) points.Add(new(1000,50));
        var context=new InstrumentalEditorContext(snapshot,"song.wav",new(TimeSpan.FromSeconds(10),[.5f]),[0],[120],TimeSpan.FromMilliseconds(1500),4,32,100,100,100,points);
        points.Clear(); var audio=new StubWorkstationAudioService();
        var vm=new InstrumentalArrangementPopupVM(context,new MarkerPlaybackCoordinator(audio,Task.Delay),new WeakReferenceMessenger(),new ImmediateUiScheduler());
        try
        {
            await vm.InitializeAsync(default);
            Assert.That(audio.ControlPoints.Count,Is.EqualTo(hasPoints ? 1 : 0));
            Assert.That(new ControlPointVolumeTimeline(audio.ControlPoints).GetGain(66150),Is.EqualTo(hasPoints ? .5f : 1f));
            Assert.That(audio.SampleUpdates.Last().Single().FileName,Is.EqualTo("piano.wav"));
            await vm.SeekCommand.ExecuteAsync(TimeSpan.FromMilliseconds(1600));
            Assert.That(audio.ControlPoints.Count,Is.EqualTo(hasPoints ? 1 : 0));
        }
        finally { await vm.DeactivateAsync(); }
    }
    [Test]
    public async Task WorkstationPassesCurrentControlPointsToArrangement()
    {
        var instrument=new InstrumentalTrack {KeyFiles=new(){{"C4","piano.wav"}},Notes=[new(){Key="C4",Timestamp=1500}]};
        var p=new ProjectData {Offsets=[0],BPMs=[120],Tracks=[instrument],ControlPoints=[new(){Timestamp=1000,Volume=50,Sampleset="Drum"}]};
        var dialogs=new StubInstrumentalTrackDialogService(); var audio=new StubWorkstationAudioService();
        var vm=new WorkstationPageVM(new StubWorkstationSourceService {Load=(_,_)=>Task.FromResult(new WorkstationSource("map","song",p))},
            new MarkerPlaybackCoordinator(audio,Task.Delay),new ImmediateUiScheduler(),messenger:new WeakReferenceMessenger(),instrumentDialogs:dialogs);
        var called=false;
        dialogs.Arrangement=(context,_)=>
        {
            called=true; Assert.That(context.ControlPoints.Single(),Is.EqualTo(new ControlPointVolumeSnapshot(1000,50)));
            Assert.That(audio.SampleUpdates.Last().Single().FileName,Is.EqualTo("piano.wav")); return Task.CompletedTask;
        };
        try
        {
            await vm.LoadAsync("project.json",default); vm.AcknowledgePresentationReady(vm.LoadGeneration);
            await vm.ViewArrangementCommand.ExecuteAsync(instrument); Assert.That(called,Is.True);
        }
        finally { await vm.DeactivateAsync(); }
    }
}
