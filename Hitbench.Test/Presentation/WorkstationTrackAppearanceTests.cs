using Hitbench.Core.Entities;
using Hitbench.Resources.DataModels;
using Hitbench.Services;
using Hitbench.ViewModels.Shell;
namespace Hitbench.Test.Presentation;
public class WorkstationTrackAppearanceTests
{
    [TestCase(false)] [TestCase(true)]
    public async Task AcceptedMappedTrackRenameSettlesOpenDrawer(bool mapping)
    {
        var track = new InstrumentalTrack { Name = "Piano", FolderName = "Piano", KeyFiles = new() { ["C3"] = "Piano/C3.wav" } };
        var entered = new TaskCompletionSource();
        var settled = false;
        async Task Drawer(CancellationToken token)
        {
            entered.TrySetResult();
            try { await Task.Delay(Timeout.Infinite, token); }
            finally { settled = true; }
        }
        var files = new StubSampleFileService(["Piano/C3.wav"]) { FolderRename = (_, _, _, _, _) =>
        { Assert.That(settled, Is.True); return Task.FromResult("Soft Piano"); } };
        var dialogs = new StubInstrumentalTrackDialogService { Arrangement = (_, token) => Drawer(token), Mapping = (_, token) => Drawer(token) };
        var vm = new WorkstationPageVM(new StubWorkstationSourceService { Load = (_, _) => Task.FromResult(new WorkstationSource("map", "song", new ProjectData { Tracks = [track], Offsets = [0], BPMs = [120] })) },
            new MarkerPlaybackCoordinator(new StubWorkstationAudioService(), Task.Delay), new ImmediateUiScheduler(), sampleFiles: files,
            messenger: new CommunityToolkit.Mvvm.Messaging.WeakReferenceMessenger(), instrumentDialogs: dialogs,
            trackAppearanceDialog: new StubOHsTrackAppearanceDialogService { Request = (_, _) => Task.FromResult<OHsTrackAppearanceResult?>(new("Soft Piano", "#123456")) });
        try
        {
            await vm.LoadAsync("project.json", default); vm.AcknowledgePresentationReady(vm.LoadGeneration);
            var drawer = mapping ? vm.KeysMappingCommand.ExecuteAsync(track) : vm.ViewArrangementCommand.ExecuteAsync(track);
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
            try { await vm.ChangeTrackNameAndColorCommand.ExecuteAsync(track).WaitAsync(TimeSpan.FromSeconds(2)); }
            finally { await vm.SettleInstrumentalEditorAsync(); }
            await drawer;
            Assert.That(track.KeyFiles["C3"], Is.EqualTo("Soft Piano/C3.wav"));
            Assert.That(track.Color, Is.EqualTo("#123456"));
        }
        finally { await vm.DeactivateAsync(); }
    }

    [TestCase(false)][TestCase(true)]
    public async Task OneSaveContainsBothFieldsAndFailureUsesExistingRecovery(bool fails)
    {
        var track=new Track{Name="Old"};var writes=0;
        var files=new StubProjectFileService((_,_,_)=>Task.FromResult("unused")) {Save=(_,json,_)=>{
            writes++;var saved=Hitbench.Services.ProjectFiles.ProjectDataSerializer.Deserialize(json).Tracks[0];
            Assert.That(saved.Name,Is.EqualTo("New"));Assert.That(saved.Color,Is.EqualTo("#123456"));
            return fails?Task.FromException(new IOException("save failed")):Task.CompletedTask;
        }};
        var vm=new WorkstationPageVM(new StubWorkstationSourceService{Load=(_,_)=>Task.FromResult(new WorkstationSource("map","song",new ProjectData{Tracks=[track],Offsets=[0],BPMs=[120]}))},new MarkerPlaybackCoordinator(new StubWorkstationAudioService(),Task.Delay),new ImmediateUiScheduler(),projectFiles:files,trackAppearanceDialog:new StubOHsTrackAppearanceDialogService{Request=(_,_)=>Task.FromResult<OHsTrackAppearanceResult?>(new("New","#123456"))});
        try
        {
            await vm.LoadAsync("project.json",default);vm.AcknowledgePresentationReady(vm.LoadGeneration);
            await vm.ChangeTrackNameAndColorCommand.ExecuteAsync(track);
            if(fails)Assert.ThrowsAsync<IOException>(async()=>await vm.FlushSavesAsync());else await vm.FlushSavesAsync();
            Assert.That(writes,Is.EqualTo(1));Assert.That(track.Name,Is.EqualTo("New"));Assert.That(track.Color,Is.EqualTo("#123456"));
        }
        finally{ files.Save=(_,_,_)=>Task.CompletedTask; try{await vm.DeactivateAsync();}catch(IOException){ } }
    }
    [TestCase("cancel")][TestCase("reload")][TestCase("deactivate")][TestCase("invalid")]
    public async Task IgnoresCancelledStaleAndInvalidResults(string action)
    {
        var track=new Track{Name="Old"}; var completion=new TaskCompletionSource<OHsTrackAppearanceResult?>();
        var source=new StubWorkstationSourceService{Load=(_,_)=>Task.FromResult(new WorkstationSource("map","song",new ProjectData{Tracks=[track],Offsets=[0],BPMs=[120]}))};
        var vm=new WorkstationPageVM(source,new MarkerPlaybackCoordinator(new StubWorkstationAudioService(),Task.Delay),new ImmediateUiScheduler(),trackAppearanceDialog:new StubOHsTrackAppearanceDialogService{Request=(_,_)=>completion.Task});
        try
        {
            await vm.LoadAsync("project.json",default);vm.AcknowledgePresentationReady(vm.LoadGeneration);
            var edit=vm.ChangeTrackNameAndColorCommand.ExecuteAsync(track);
            Task? settle=null;
            if(action=="reload")settle=vm.LoadAsync("other.json",default);
            if(action=="deactivate")settle=vm.DeactivateAsync();
            completion.SetResult(action=="cancel"?null:new("New",action=="invalid"?"bad":"#123456"));
            await edit;if(settle is not null)await settle;
            Assert.That(track.Name,Is.EqualTo("Old"));Assert.That(track.Color,Is.Null);
        }
        finally{completion.TrySetResult(null);await vm.DeactivateAsync();}
    }
    [TestCase(false,false)][TestCase(true,false)][TestCase(false,true)][TestCase(true,true)] public async Task CommitsOnlyAcceptedLiveDraft(bool removed, bool instrumental)
    {
        Track track=instrumental ? new InstrumentalTrack{Name="Old"} : new Track{Name="Old"};
        var completion=new TaskCompletionSource<OHsTrackAppearanceResult?>();
        var dialog=new StubOHsTrackAppearanceDialogService{Request=(_,_)=>completion.Task};
        var source=new StubWorkstationSourceService{Load=(_,_)=>Task.FromResult(new WorkstationSource("map","song",new ProjectData{Tracks=[track],Offsets=[0],BPMs=[120]}))};
        var vm=new WorkstationPageVM(source,new MarkerPlaybackCoordinator(new StubWorkstationAudioService(),Task.Delay),new ImmediateUiScheduler(),trackAppearanceDialog:dialog);
        try
        {
            await vm.LoadAsync("project.json",default); vm.AcknowledgePresentationReady(vm.LoadGeneration);
            var edit=vm.ChangeTrackNameAndColorCommand.ExecuteAsync(track);
            Assert.That(track.Name,Is.EqualTo("Old"));
            Assert.That(vm.ChangeTrackNameAndColorCommand.CanExecute(track),Is.False);
            if(removed)vm.Tracks.Remove(track);
            completion.SetResult(new("New","#abcdef")); await edit;
            Assert.That(track.Name,Is.EqualTo(removed?"Old":"New"));
            Assert.That(track.Color,Is.EqualTo(removed?null:"#ABCDEF"));
        }
        finally{completion.TrySetResult(null);await vm.DeactivateAsync();}
    }
}
