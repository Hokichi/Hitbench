using osuHitsounder.Core.Entities;
using osuHitsounder.Services;
using osuHitsounder.ViewModels.Shell;
namespace osuHitsounder.Test.Presentation;
public class WorkstationInstrumentalPlaybackTests
{
 [Test] public async Task GlobalPlaybackContainsInstrumentAndHonorsMute()
 {
  var instrument=new InstrumentalTrack { KeyFiles=new(){["C4"]="piano.wav"},Notes=[new(){Key="C4",Timestamp=125}] };
  var project=new ProjectData { Tracks=[instrument],Offsets=[0],BPMs=[120] }; var audio=new StubWorkstationAudioService();
  var owner=new WorkstationPageVM(new StubWorkstationSourceService { Load=(_,_)=>Task.FromResult(new WorkstationSource("map","song",project)) },new MarkerPlaybackCoordinator(audio,Task.Delay),new ImmediateUiScheduler(),sampleFiles:new StubSampleFileService(["piano.wav"]));
  try {
   await owner.LoadAsync("project.json",default); owner.AcknowledgePresentationReady(owner.LoadGeneration);
   Assert.That(audio.SampleUpdates.Last().Single().FileName,Is.EqualTo("piano.wav"));
   var id=audio.SampleUpdates.Last().Single().Id;
   owner.SetTrackMuted(instrument,true); await owner.FlushPlaybackUpdatesAsync(); Assert.That(audio.SampleUpdates.Last(),Is.Empty);
   owner.SetTrackMuted(instrument,false); await owner.FlushPlaybackUpdatesAsync(); Assert.That(audio.SampleUpdates.Last().Single().Id,Is.EqualTo(id));
  } finally { await owner.DeactivateAsync(); }
 }
}
