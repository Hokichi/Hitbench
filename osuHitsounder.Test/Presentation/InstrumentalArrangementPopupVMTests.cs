using CommunityToolkit.Mvvm.Messaging;
using osuHitsounder.Core.Entities;
using osuHitsounder.Resources.DataModels;
using osuHitsounder.Resources.Messages;
using osuHitsounder.Services;
using osuHitsounder.ViewModels.Popups;
namespace osuHitsounder.Test.Presentation;
public class InstrumentalArrangementPopupVMTests
{
 [Test] public async Task LocalTransportUsesOnlyCurrentInstrumentAndSaveStatusDoesNotRebuild()
 {
  var ctx=new InstrumentalTrackContext("project.json",null,Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid());
  var snap=new InstrumentalTrackSnapshot(ctx,"Piano",75,new Dictionary<string,string>{["C4"]="piano.wav"},[new(Guid.NewGuid(),"C4",125,70)],["piano.wav"],0);
  var data=new InstrumentalEditorContext(snap,"song.wav",new(TimeSpan.FromSeconds(10),[.5f]),[0],[120],TimeSpan.FromSeconds(1),4,32,90,80,100);
  var messenger=new WeakReferenceMessenger();var audio=new StubWorkstationAudioService();
  var vm=new InstrumentalArrangementPopupVM(data,new MarkerPlaybackCoordinator(audio,Task.Delay),messenger,new ImmediateUiScheduler());
  try {
   await vm.InitializeAsync(default);Assert.That(audio.SampleUpdates.Last().Single().TrackVolume,Is.EqualTo(75));
   var updates=audio.SampleUpdates.Count;messenger.Send(new InstrumentalSaveStateChangedMessage(ctx,1,true,null));
   Assert.That(audio.SampleUpdates.Count,Is.EqualTo(updates));
   await vm.SeekCommand.ExecuteAsync(TimeSpan.FromSeconds(2));Assert.That(vm.Position,Is.EqualTo(TimeSpan.FromSeconds(2)));
   await vm.StopCommand.ExecuteAsync(null);Assert.That(vm.Position,Is.EqualTo(TimeSpan.Zero));
  } finally {await vm.DeactivateAsync();}
  Assert.That(audio.Disposed,Is.True);
 }
}
