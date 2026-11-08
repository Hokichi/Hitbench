using CommunityToolkit.Mvvm.Messaging;
using Hitbench.Resources.Messages;
using Hitbench.Services.FileSystem;
using Hitbench.ViewModels.Popups;
namespace Hitbench.Test.Presentation;
public sealed class ManageMapsetSamplesTests
{
    [TestCase(false)] [TestCase(true)]
    public async Task GroupsKeepCanonicalPathsAndExcludeInstrumentFolders(bool conflict)
    {
        var names=new List<string>{"hit.wav","Mapset/drum-hitnormal9.wav","Piano/C3.wav"};
        var files=new StubSampleFileService(names);
        var messenger=new WeakReferenceMessenger();
        var vm=new ManageSamplesPopupVM(new("project.json",null,Guid.NewGuid(),conflict),files,new(files),new StubSampleFileDialogService([]),new StubSampleRemovalDialogService(),messenger,new ImmediateUiScheduler());
        try
        {
            await vm.InitializeAsync(default);
            Assert.That(vm.Entries.Select(e=>e.FileName),Is.EqualTo(new[]{"hit.wav"}));
            Assert.That(vm.MapsetEntries.Count,Is.EqualTo(conflict?0:1));
            if(conflict) return;
            var row=vm.MapsetEntries.Single(); Assert.That(row.DisplayFileName,Is.EqualTo("drum-hitnormal9.wav"));
            Assert.That(vm.GetTrackDragData(row)!.FileName,Is.EqualTo("Mapset/drum-hitnormal9.wav"));
            files.Rename=(_,old,stem,_)=>{ names.Remove(old);var name="Mapset/"+stem+".wav";names.Add(name);return Task.FromResult(name); };
            await vm.EditCommand.ExecuteAsync(row);vm.DraftStem="renamed";await vm.CommitRenameCommand.ExecuteAsync(null);
            Assert.That(vm.MapsetEntries.Single().FileName,Is.EqualTo("Mapset/renamed.wav"));
            Assert.That(vm.MapsetEntries.Single().DisplayFileName,Is.EqualTo("renamed.wav"));
            messenger.Send(new ProjectSamplesChangedMessage(vm.Context,["other.wav"],includesSubfolders:false));
            Assert.That(vm.MapsetEntries.Single().FileName,Is.EqualTo("Mapset/renamed.wav"));
            Assert.That(vm.Entries.Single().FileName,Is.EqualTo("other.wav"));
        }
        finally { await vm.DeactivateAsync(); }
    }
}
