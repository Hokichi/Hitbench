using System.Windows.Controls;
using System.Windows.Input;
using Hitbench.Resources.CustomControls;
using Hitbench.Resources.DataModels;
using Hitbench.ViewModels.Popups;
using Hitbench.Views.Popups;

namespace Hitbench.Test.Presentation;

[Apartment(ApartmentState.STA), NonParallelizable]
public class OHsMidiImportPopupIntegrationTests
{
    [Test]
    public void KeyboardChoiceAcceptsOnlyOnceAndUsesSharedContracts()
    {
        new VolumeWheelTests().SetUp();
        var vm = new OHsMidiImportPopupVM(Services.OHsMidiImportMapperTests.Source(), [new() { Offset = 0, BPM = 180 }]);
        var completions = new List<bool>();
        var popup = new OHsMidiImportPopup(vm, accepted => completions.Add(accepted));
        using var view = new InMemoryView(popup, scheduleLayout: false);
        view.Root.Resources.MergedDictionaries.Add(popup.Resources);
        var modes = (SegmentedToggleGroup)popup.FindName("TimingModes");
        UiControlContract.AssertStyle<SegmentedToggleGroup>(modes, "OHsMidiModesStyle");
        foreach (var option in modes.Items.OfType<SegmentedToggleOption>())
        {
            UiControlContract.AssertStyle<SegmentedToggleOption>(option, "OHsMidiModeStyle");
            Assert.That(option.IsTabStop, Is.False);
        }
        var import = (BalloonButton)popup.FindName("ImportAction");
        UiControlContract.AssertStyle<BalloonButton>(import, "OHsBalloonConfirmActionStyle");
        Assert.That(import.IsTabStop, Is.False);
        Assert.That(import.ButtonIcon, Is.SameAs(import.FindResource("Download")));
        Assert.That(WorkstationControlTree.Descendants(view.Root).OfType<Control>().All(c => !c.IsTabStop), Is.True);
        popup.HandleKey(Key.Down); popup.HandleKey(Key.Down);
        Assert.That(vm.SelectedMode, Is.EqualTo(OHsMidiImportMode.ProjectBpm));
        popup.HandleKey(Key.Enter); popup.HandleKey(Key.Enter);
        Assert.That(completions, Is.EqualTo(new[] { true }));
    }

    [TestCase(Key.Escape)]
    [TestCase(Key.Enter)]
    public void CancelDiscardsSelection(Key key)
    {
        new VolumeWheelTests().SetUp();
        bool? accepted = null;
        var popup = new OHsMidiImportPopup(new(Services.OHsMidiImportMapperTests.Source(), [new() { Offset = 0, BPM = 180 }]), value => accepted = value);
        using var view = new InMemoryView(popup, scheduleLayout: false);
        popup.HandleKey(key, (BalloonButton)popup.FindName("CloseAction"));
        Assert.That(accepted, Is.False);
    }
}
