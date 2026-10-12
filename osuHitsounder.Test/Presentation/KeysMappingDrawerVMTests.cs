using CommunityToolkit.Mvvm.Messaging;
using osuHitsounder.Core.Entities;
using osuHitsounder.Services;
using osuHitsounder.ViewModels.Shell;
using osuHitsounder.ViewModels.Popups;
namespace osuHitsounder.Test.Presentation;
public class KeysMappingDrawerVMTests
{
 [Test] public async Task ValidMappingAutosavesAndInvalidDraftPreservesOldFile()
 {
  var track=new InstrumentalTrack { KeyFiles=new(){["C1"]="old.wav"} };
  var project=new ProjectData { Tracks=[track],Offsets=[0],BPMs=[120] };
  var messenger=new WeakReferenceMessenger(); var dialogs=new StubInstrumentalTrackDialogService();
  var files=new StubSampleFileService(["old.wav","new.wav"]);
  var owner=new WorkstationPageVM(new StubWorkstationSourceService { Load=(_,_)=>Task.FromResult(new WorkstationSource("map","song",project)) },
   new MarkerPlaybackCoordinator(new StubWorkstationAudioService(),Task.Delay),new ImmediateUiScheduler(),
   sampleFiles:files,messenger:messenger,instrumentDialogs:dialogs);
  dialogs.Mapping=async(snapshot,token)=>
  {
   var vm=new KeysMappingDrawerVM(snapshot,messenger,new ImmediateUiScheduler(),files,new StubSampleFileDialogService("new.wav"));
   try {
    Assert.That(vm.Rows.Count,Is.EqualTo(108));
    Assert.That(vm.CommitMapping("C1","../bad.wav"),Is.False);
    Assert.That(track.KeyFiles["C1"],Is.EqualTo("old.wav"));
    await vm.OpenKeyAsync("C1",token); Assert.That(track.KeyFiles["C1"],Is.EqualTo("new.wav"));
    vm.Rows[0].DraftFileName=""; vm.PrepareClose(); Assert.That(track.KeyFiles.ContainsKey("C1"),Is.False);
   } finally { await vm.DeactivateAsync(); }
  };
  try { await owner.LoadAsync("project.json",default);owner.AcknowledgePresentationReady(owner.LoadGeneration);await owner.KeysMappingCommand.ExecuteAsync(track); }
  finally { await owner.DeactivateAsync(); }
 }
}
