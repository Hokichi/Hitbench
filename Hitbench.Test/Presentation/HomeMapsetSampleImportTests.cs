using CommunityToolkit.Mvvm.Messaging;
using Hitbench.Resources.DataModels;
using Hitbench.Resources.Messages;
using Hitbench.Services.FileSystem;
using Hitbench.ViewModels.Shell.HomePage;
namespace Hitbench.Test.Presentation;
public sealed class HomeMapsetSampleImportTests
{
    [TestCase(false)] [TestCase(true)]
    public async Task CreationWaitsForSelectionAndOpensAfterAcceptOrReject(bool accept)
    {
        var root = Directory.CreateTempSubdirectory("Hitbench-home-mapset-");
        var selection = new TaskCompletionSource<IReadOnlyList<string>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        HomePageVM? vm = null;
        try
        {
            var source = Path.Combine(root.FullName,"drum-hitnormal9.wav"); await File.WriteAllTextAsync(source,"sample");
            var path = Path.Combine(root.FullName,"project","project.json");
            var files = new SampleFileService(root.FullName);
            var dialog = new StubOHsMapsetSampleImportDialogService { Select = (_,_) => { entered.TrySetResult(); return selection.Task; } };
            var messenger = new WeakReferenceMessenger(); var created = new List<string>();
            messenger.Register<ProjectCreatedMessage>(created,(_,m)=>created.Add(m.ProjectPath));
            vm = new(new StubHomeFolderService(),new StubAppSettingsService(),new StubInstallationDialogService(),new ImmediateUiScheduler(),
                new StubProjectFileService((_,_,_)=>Task.FromResult(path)),messenger,mapsetImports:new(files,dialog));
            vm.SelectedMapset = new FolderEntry(root.FullName,"song",default);
            await vm.ShowProjectCreationCommand.ExecuteAsync(null); vm.DraftProjectName="new";
            var creating=vm.CreateProjectCommand.ExecuteAsync(null); await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.That(created,Is.Empty); Assert.That(vm.IsCreatingProject,Is.True);
            selection.SetResult(accept ? new[]{source}:Array.Empty<string>()); await creating;
            Assert.That(created,Is.EqualTo(new[]{path}));
            Assert.That(File.Exists(Path.Combine(root.FullName,"project","Samples","Mapset","drum-hitnormal9.wav")),Is.EqualTo(accept));
        }
        finally { selection.TrySetResult([]); vm?.Deactivate(); if(vm?.CreateProjectCommand.ExecutionTask is { } pending) await pending; root.Delete(true); }
    }

    [TestCase(false)] [TestCase(true)]
    public async Task ImportFailureDoesNotFailCreatedProjectAndDeactivationSuppressesNavigation(bool deactivate)
    {
        var root=Directory.CreateTempSubdirectory("Hitbench-home-mapset-error-");
        var gate=new TaskCompletionSource<IReadOnlyList<string>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var entered=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        HomePageVM? vm=null;
        try
        {
            var source=Path.Combine(root.FullName,"drum-hitnormal9.wav");await File.WriteAllTextAsync(source,"sample");
            var files=new StubSampleFileService([]) { Import=(_,_,_)=>Task.FromException<string>(new IOException("copy denied")) };
            var dialog=new StubOHsMapsetSampleImportDialogService { Select=(_,_)=>{entered.TrySetResult();return gate.Task;} };
            var messenger=new WeakReferenceMessenger();var created=new List<ProjectCreatedMessage>();var failures=new List<OHsSampleImportFailedMessage>();var creationErrors=new List<ProjectCreationFailedMessage>();
            messenger.Register<ProjectCreatedMessage>(created,(_,m)=>created.Add(m));
            messenger.Register<OHsSampleImportFailedMessage>(failures,(_,m)=>failures.Add(m));
            messenger.Register<ProjectCreationFailedMessage>(creationErrors,(_,m)=>creationErrors.Add(m));
            vm=new(new StubHomeFolderService(),new StubAppSettingsService(),new StubInstallationDialogService(),new ImmediateUiScheduler(),
                new StubProjectFileService((_,_,_)=>Task.FromResult("created.json")),messenger,mapsetImports:new(files,dialog));
            vm.SelectedMapset=new FolderEntry(root.FullName,"song",default);await vm.ShowProjectCreationCommand.ExecuteAsync(null);vm.DraftProjectName="new";
            var task=vm.CreateProjectCommand.ExecuteAsync(null);await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            if(deactivate) vm.Deactivate();gate.SetResult([source]);await task;
            Assert.That(created.Count,Is.EqualTo(deactivate?0:1));Assert.That(failures.Count,Is.EqualTo(deactivate?0:1));
            Assert.That(creationErrors,Is.Empty);Assert.That(vm.IsCreatingProject,Is.False);
        }
        finally {gate.TrySetResult([]);vm?.Deactivate();if(vm?.CreateProjectCommand.ExecutionTask is {} task)await task;root.Delete(true);}
    }
}
