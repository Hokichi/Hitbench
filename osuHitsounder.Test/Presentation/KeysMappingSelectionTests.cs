using CommunityToolkit.Mvvm.Messaging;
using osuHitsounder.Resources.DataModels;
using osuHitsounder.ViewModels.Popups;
namespace osuHitsounder.Test.Presentation;
public class KeysMappingSelectionTests
{
    [Test]
    public async Task SelectionStartsEmptyAndInvalidDraftKeepsOriginalKey()
    {
        var context = new InstrumentalTrackContext("project.json", null, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        var vm = new KeysMappingDrawerVM(new(context,"Piano",100,new Dictionary<string,string>(),[],[],0),new WeakReferenceMessenger(),new ImmediateUiScheduler(),new StubSampleFileService([]),new StubSampleFileDialogService((string?)null));
        try {
            Assert.That(vm.SelectedEntry, Is.Null);
            Assert.That(await vm.SelectKeyAsync("C5"), Is.True);
            vm.SelectedEntry!.DraftFileName = "../bad.wav";
            Assert.That(await vm.SelectKeyAsync("D5"), Is.False);
            Assert.That(vm.SelectedEntry.Key, Is.EqualTo("C5"));
        } finally { await vm.DeactivateAsync(); }
    }
}
