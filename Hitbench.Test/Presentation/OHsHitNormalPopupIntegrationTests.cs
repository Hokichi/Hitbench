using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using Hitbench.Resources.CustomControls;
using Hitbench.Resources.DataModels;
using Hitbench.ViewModels.Popups;
using Hitbench.Views.Popups;

namespace Hitbench.Test.Presentation;

[Apartment(ApartmentState.STA), NonParallelizable]
public class OHsHitNormalPopupIntegrationTests
{
    [TestCase(false)][TestCase(true)]
    public void BoundSelectionsAndKeyboardCompleteDraftWithoutShowingWindows(bool save)
    {
        new VolumeWheelTests().SetUp();
        var vm = new OHsHitNormalPopupVM(new(), [new("a.wav", "Samples/a.wav")]);
        bool? result = null;
        var popup = new OHsHitNormalPopup(vm, accepted => result = accepted);
        using var view = new InMemoryView(popup, scheduleLayout: false);
        view.Root.Resources.MergedDictionaries.Add(popup.Resources);
        foreach (var name in new[] { "Normal", "Soft", "Drum" })
        {
            var combo = (ComboBox)popup.FindName(name + "Input");
            UiControlContract.AssertStyle<ComboBox>(combo, "OHsHitNormalComboBoxStyle");
            Assert.That(AutomationProperties.GetName(combo), Is.EqualTo(name));
            Assert.That(combo.IsTabStop, Is.False);
            Assert.That(combo.ItemsPanel, Is.SameAs(combo.FindResource("OHsVirtualizedDrawerItemsPanel")));
            Assert.That(VirtualizingPanel.GetVirtualizationMode(combo), Is.EqualTo(VirtualizationMode.Recycling));
            combo.SelectedValue = "a.wav";
        }
        Assert.That(vm.CreateResult(), Is.EqualTo(new OHsHitNormalConfiguration("a.wav", "a.wav", "a.wav")));
        popup.HandleKey(save ? Key.Enter : Key.Escape);
        Assert.That(result, Is.EqualTo(save));
        Assert.That(popup.IsVisible, Is.False);
        UiControlContract.AssertStyle<BalloonButton>((BalloonButton)popup.FindName("SaveAction"), "OHsBalloonConfirmActionStyle");
    }

    [TestCase(FlowDirection.LeftToRight)][TestCase(FlowDirection.RightToLeft)]
    public void LargeCatalogAndExplicitSmallWorkAreaPreserveChoices(FlowDirection direction)
    {
        new VolumeWheelTests().SetUp();
        var vm = new OHsHitNormalPopupVM(new(), Enumerable.Range(0, 3000).Select(i => new OHsHitNormalChoice($"{i}.wav", $"Samples/{i}.wav")).ToArray());
        var popup = new OHsHitNormalPopup(vm, _ => { }) { FlowDirection = direction };
        popup.ApplyWorkArea(new Size(320, 240), 96);
        using var view = new InMemoryView(popup, scheduleLayout: false);
        var combo = (ComboBox)popup.FindName("NormalInput");
        Assert.That(combo.Items.Count, Is.EqualTo(3001));
        combo.SelectedValue = "2999.wav";
        Assert.That(vm.NormalPath, Is.EqualTo("2999.wav"));
        Assert.That(combo.FlowDirection, Is.EqualTo(direction));
        Assert.That(combo.IsTabStop, Is.False);
    }
}
