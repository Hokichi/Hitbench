using CommunityToolkit.Mvvm.Messaging;
using Hitbench.Core.Entities;
using Hitbench.Services;
using Hitbench.Services.FileSystem;
using Hitbench.ViewModels.Shell;
namespace Hitbench.Test.Presentation;
public sealed class WorkstationMapsetSampleImportTests
{
    [TestCase(false)] [TestCase(true)]
    public async Task ImportedMapsetFilesBecomeTrackChoicesButInstrumentFoldersDoNot(bool conflict)
    {
        var root=Directory.CreateTempSubdirectory("Hitbench-work-mapset-");
        WorkstationPageVM? vm=null;
        try
        {
            var song=root.CreateSubdirectory("song"); await File.WriteAllTextAsync(Path.Combine(song.FullName,"drum-hitnormal9.wav"),"sample");
            var path=Path.Combine(root.FullName,"project","project.json");
            var project=new ProjectData { BeatmapPath=song.FullName,Offsets=[0],BPMs=[120],ControlPoints=[new()] };
            if(conflict) project.Tracks.Add(new InstrumentalTrack { FolderName="Mapset" });
            var files=new SampleFileService(root.FullName);
            var audio = new StubWorkstationAudioService();
            vm=new(new StubWorkstationSourceService { Load=(_,_)=>Task.FromResult(new WorkstationSource("map","song",project)) },
                new MarkerPlaybackCoordinator(audio,Task.Delay),new ImmediateUiScheduler(),files,
                messenger:new WeakReferenceMessenger(),mapsetImports:new(files,new StubOHsMapsetSampleImportDialogService()));
            await vm.LoadAsync(path,default); vm.AcknowledgePresentationReady(vm.LoadGeneration);
            Assert.That(vm.ImportFromMapsetCommand.CanExecute(null),Is.EqualTo(!conflict));
            if(!conflict)
            {
                await vm.ImportFromMapsetCommand.ExecuteAsync(null);
                Assert.That(vm.SampleFiles,Does.Contain("Mapset/drum-hitnormal9.wav"));
                vm.AddSampleTrackCommand.Execute("Mapset/drum-hitnormal9.wav");
                await vm.FlushPlaybackUpdatesAsync();
                var before = audio.ForceReloadSampleRequests;
                await File.WriteAllTextAsync(Path.Combine(song.FullName, "drum-hitnormal9.wav"), "replacement");
                await vm.ImportFromMapsetCommand.ExecuteAsync(null);
                Assert.That(audio.ForceReloadSampleRequests, Is.EqualTo(before + 1));
                Assert.That(await File.ReadAllTextAsync(Path.Combine(root.FullName, "project", "Samples", "Mapset", "drum-hitnormal9.wav")), Is.EqualTo("replacement"));
                Assert.That(vm.SampleFiles.Any(p=>p.StartsWith("Piano/")),Is.False);
                await vm.LoadAsync(path,default); vm.AcknowledgePresentationReady(vm.LoadGeneration);
                Assert.That(vm.SampleFiles,Does.Contain("Mapset/drum-hitnormal9.wav"));
            }
        }
        finally { if(vm is not null) await vm.DeactivateAsync(); root.Delete(true); }
    }

    [TestCase(false)] [TestCase(true)]
    public async Task LateSelectionCannotCopyOrRefreshReplacementProject(bool fail)
    {
        var root=Directory.CreateTempSubdirectory("Hitbench-work-mapset-cancel-");
        var gate=new TaskCompletionSource<IReadOnlyList<string>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var entered=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        WorkstationPageVM? vm=null;
        try
        {
            var source=Path.Combine(root.FullName,"drum-hitnormal9.wav");await File.WriteAllTextAsync(source,"sample");
            var copied=0;var files=new StubSampleFileService([]) { Import=(_,_,_)=>{copied++;return Task.FromResult("Mapset/drum-hitnormal9.wav");} };
            var dialog=new StubOHsMapsetSampleImportDialogService { Select=(_,_)=>{entered.TrySetResult();return gate.Task;} };
            vm=new(new StubWorkstationSourceService { Load=(_,_)=>Task.FromResult(new WorkstationSource("map","song",new ProjectData { BeatmapPath=root.FullName,Offsets=[0],BPMs=[120] })) },
                new MarkerPlaybackCoordinator(new StubWorkstationAudioService(),Task.Delay),new ImmediateUiScheduler(),files,mapsetImports:new(files,dialog));
            Assert.That(vm.CanImportFromMapset,Is.False);
            await vm.LoadAsync("old.json",default);vm.AcknowledgePresentationReady(vm.LoadGeneration);
            var importing=vm.ImportFromMapsetCommand.ExecuteAsync(null);await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));Assert.That(vm.CanImportFromMapset,Is.False);
            var replacing=vm.LoadAsync("new.json",default);
            if(fail)gate.SetException(new IOException("late failure"));else gate.SetResult([source]);
            await importing;await replacing;vm.AcknowledgePresentationReady(vm.LoadGeneration);
            Assert.That(copied,Is.Zero);Assert.That(vm.SampleFiles,Is.Empty);Assert.That(vm.CanImportFromMapset,Is.True);
            Assert.That(vm.ErrorMessage,Is.Not.EqualTo("late failure"));
        }
        finally {gate.TrySetResult([]);if(vm is not null)await vm.DeactivateAsync();root.Delete(true);}
    }
    [TestCase(false)] [TestCase(true)]
    public async Task DrawerReservationIsRequiredAndReleased(bool allow)
    {
        var root=Directory.CreateTempSubdirectory("Hitbench-work-mapset-reserve-");
        var release=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var opened=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        WorkstationPageVM? vm=null;
        try
        {
            await File.WriteAllTextAsync(Path.Combine(root.FullName,"drum-hitnormal9.wav"),"sample");
            var messenger=new WeakReferenceMessenger();var recipient=new object();var finished=0;var presented=0;var changes=0;
            messenger.Register<Hitbench.Resources.Messages.OHsPrepareSampleDropImportMessage>(recipient,(_,m)=>m.Reply(Task.FromResult(allow)));
            messenger.Register<Hitbench.Resources.Messages.OHsSampleDropImportFinishedMessage>(recipient,(_,_)=>finished++);
            messenger.Register<Hitbench.Resources.Messages.ProjectSamplesChangedMessage>(recipient,(_,_)=>changes++);
            var files=new StubSampleFileService([]);
            var dialog=new StubOHsMapsetSampleImportDialogService { Select=(_,_)=>{presented++;return Task.FromResult<IReadOnlyList<string>>([]);} };
            var manager=new StubSampleManagementDialogService { Present=(_,token)=>{opened.TrySetResult();return release.Task.WaitAsync(token);} };
            vm=new(new StubWorkstationSourceService { Load=(_,_)=>Task.FromResult(new WorkstationSource("map","song",new ProjectData { BeatmapPath=root.FullName,Offsets=[0],BPMs=[120] })) },
                new MarkerPlaybackCoordinator(new StubWorkstationAudioService(),Task.Delay),new ImmediateUiScheduler(),files,new StubSampleFileDialogService([]),
                messenger:messenger,sampleManagementDialog:manager,mapsetImports:new(files,dialog));
            await vm.LoadAsync("project.json",default);vm.AcknowledgePresentationReady(vm.LoadGeneration);
            await vm.ManageSamplesCommand.ExecuteAsync(null);await opened.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await vm.ImportFromMapsetCommand.ExecuteAsync(null);
            Assert.That(presented,Is.EqualTo(allow?1:0));Assert.That(finished,Is.EqualTo(allow?1:0));Assert.That(vm.CanImportFromMapset,Is.True);
            Assert.That(changes,Is.Zero,"Rejecting import must not publish a project mutation.");
        }
        finally {release.TrySetResult();if(vm is not null)await vm.DeactivateAsync();root.Delete(true);}
    }
}
