using Hitbench.Core.Entities;
using Hitbench.Services;
using Hitbench.ViewModels.Shell;
using Newtonsoft.Json.Linq;
namespace Hitbench.Test.Presentation;
public class WorkstationTimingMigrationTests
{
 [TestCase(false), TestCase(true)]
 public async Task MigrationSaveIsAwaitedAndPreservesLastChanged(bool fail)
 {
  var changed=DateTimeOffset.Parse("2020-01-02T00:00:00Z");
  var project=new ProjectData {LastChanged=changed, UninheritedPoints=[new(){Offset=0,BPM=120}], ControlPoints=[new(){Timestamp=0}]};
  var entered=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
  var release=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
  var files=new StubProjectFileService((_,_,_)=>Task.FromResult("unused"));
  WorkstationPageVM? vm=null;
  files.Save=async (_,json,token)=> {
   Assert.That(vm!.LoadPhase,Is.EqualTo(ProjectLoadPhase.PreparingData));
   var root=JObject.Parse(json); Assert.That(root["Offsets"],Is.Null); Assert.That(root["BPMs"],Is.Null);
   Assert.That(root["UninheritedPoints"],Is.Not.Null);
   Assert.That(root["LastChanged"]!.ToObject<DateTimeOffset>(),Is.EqualTo(changed));
   entered.SetResult(); await release.Task.WaitAsync(token); if(fail) throw new IOException("migration failed");
  };
  vm=new(new StubWorkstationSourceService {Load=(_,_)=>Task.FromResult(new WorkstationSource("map","song",project,true))},new MarkerPlaybackCoordinator(new StubWorkstationAudioService(),Task.Delay),new ImmediateUiScheduler(),projectFiles:files);
  try {
   var load=vm.LoadAsync("project.json",default); await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
   Assert.That(load.IsCompleted,Is.False); Assert.That(vm.CanPlay,Is.False);
   release.SetResult(); await load;
   Assert.That(vm.LoadPhase,Is.EqualTo(fail?ProjectLoadPhase.Failed:ProjectLoadPhase.AwaitingPresentation));
   Assert.That(project.LastChanged,Is.EqualTo(changed));
  } finally {release.TrySetResult(); await vm.DeactivateAsync();}
 }
 [Test] public async Task CancelledPreparationDoesNotSaveMigration() {
  using var cancellation=new CancellationTokenSource(); var saves=0;
  var files=new StubProjectFileService((_,_,_)=>Task.FromResult("unused")){Save=(_,_,_)=>{saves++;return Task.CompletedTask;}};
  var audio=new StubWorkstationAudioService {Analyze=(_,token)=> {cancellation.Cancel(); return Task.FromCanceled<WaveformData>(token);}};
  var vm=new WorkstationPageVM(new StubWorkstationSourceService {Load=(_,_)=>Task.FromResult(new WorkstationSource("map","song",new ProjectData {UninheritedPoints=[new(){Offset=0,BPM=120}]},true))},new MarkerPlaybackCoordinator(audio,Task.Delay),new ImmediateUiScheduler(),projectFiles:files);
  try {await vm.LoadAsync("project.json",cancellation.Token); Assert.That(saves,Is.Zero);Assert.That(vm.LoadPhase,Is.EqualTo(ProjectLoadPhase.Cancelled));}finally{await vm.DeactivateAsync();}
 }

 [Test] public async Task RealLegacyProjectIsMigratedOnceUsingBeatmapTiming() {
  var root=Directory.CreateTempSubdirectory("timing-load-");
  WorkstationPageVM? vm=null;
  try {
   var song=Directory.CreateDirectory(Path.Combine(root.FullName,"song"));
   await File.WriteAllTextAsync(Path.Combine(song.FullName,"a.osu"),"[General]\nAudioFilename: song.wav\n[TimingPoints]\n0,400,4,1,0,100,1,0\n");
   await File.WriteAllTextAsync(Path.Combine(song.FullName,"song.wav"),"stub audio");
   var path=Path.Combine(root.FullName,"project.json");
   var legacy=new JObject { ["BeatmapPath"]=song.FullName,["Offsets"]=new JArray(0),["BPMs"]=new JArray(120),["LastChanged"]="2020-01-02T00:00:00Z"};
   await File.WriteAllTextAsync(path,legacy.ToString());
   var source=new Hitbench.Services.FileSystem.WorkstationSourceService(new StubSampleFileService([]));
   vm=new(source,new MarkerPlaybackCoordinator(new StubWorkstationAudioService(),Task.Delay),new ImmediateUiScheduler(),projectFiles:new Hitbench.Services.ProjectFiles.ProjectFileService(root.FullName));
   await vm.LoadAsync(path,default);
   Assert.That(vm.LoadPhase,Is.EqualTo(ProjectLoadPhase.AwaitingPresentation));
   var json=await File.ReadAllTextAsync(path);
   var loaded=Hitbench.Services.ProjectFiles.ProjectDataSerializer.Deserialize(json,out var again);
   Assert.That(again,Is.False);Assert.That(loaded.UninheritedPoints.Single().BPM,Is.EqualTo(150));
   Assert.That(loaded.LastChanged,Is.EqualTo(DateTimeOffset.Parse("2020-01-02T00:00:00Z")));
  }finally{if(vm is not null)await vm.DeactivateAsync();root.Delete(true);}
 }

 [Test] public async Task SupersededLoadCannotPersistMigration() {
  var entered=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
  var release=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
  var calls=0;var saves=0;
  var files=new StubProjectFileService((_,_,_)=>Task.FromResult("unused")){Save=(_,_,_)=>{saves++;return Task.CompletedTask;}};
  var source=new StubWorkstationSourceService {Load=(_,_)=>Task.FromResult(new WorkstationSource("map","song",new ProjectData {UninheritedPoints=[new(){Offset=0,BPM=120}],ControlPoints=[new(){Timestamp=0}]},++calls==1))};
  var analyses=0;
  var audio=new StubWorkstationAudioService {Analyze=async (_,_)=>{if(++analyses==1){entered.SetResult();await release.Task;}return new WaveformData(TimeSpan.FromSeconds(10),[0.5f]);}};
  var vm=new WorkstationPageVM(source,new MarkerPlaybackCoordinator(audio,Task.Delay),new ImmediateUiScheduler(),projectFiles:files);
  try {
   var old=vm.LoadAsync("old.json",default);await entered.Task;
   await vm.LoadAsync("new.json",default);release.SetResult();await old;
   Assert.That(saves,Is.Zero);Assert.That(vm.LoadPhase,Is.EqualTo(ProjectLoadPhase.AwaitingPresentation));
  }finally{release.TrySetResult();await vm.DeactivateAsync();}
 }
}
