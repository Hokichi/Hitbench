using Hitbench.ViewModels.Popups;
namespace Hitbench.Test.Presentation;

public sealed class OHsMapsetSampleImportPopupVMTests
{
    [Test]
    public void SelectionIsCheckedByDefaultAndCompletesOnce()
    {
        var vm = new OHsMapsetSampleImportPopupVM(["drum-hitnormal9.wav", "soft-hitclap.wav"]);
        Assert.That(vm.PromptText, Is.EqualTo("2 samples found. Would you like to import"));
        Assert.That(vm.Entries.All(e => e.IsChecked), Is.True);
        vm.Entries[0].IsChecked = false;
        Assert.That(vm.TryCompleteSelection(true), Is.True);
        Assert.That(vm.SelectedPaths, Is.EqualTo(new[] { "soft-hitclap.wav" }));
        Assert.That(vm.TryCompleteSelection(false), Is.False);
    }

    [Test]
    public void EmptySelectionCannotConfirmAndNoReturnsNoPaths()
    {
        var vm = new OHsMapsetSampleImportPopupVM(["drum-hitnormal9.wav"]);
        vm.Entries[0].IsChecked = false;
        Assert.That(vm.CanConfirm, Is.False);
        Assert.That(vm.TryCompleteSelection(true), Is.False);
        Assert.That(vm.TryCompleteSelection(false), Is.True);
        Assert.That(vm.SelectedPaths, Is.Empty);
    }
}
