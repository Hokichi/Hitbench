using osuHitsounder.Services;
using osuHitsounder.ViewModels.Popups;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using osuHitsounder.Resources.CustomControls;
using osuHitsounder.Views.Popups;

namespace osuHitsounder.Test.Presentation;

[Apartment(ApartmentState.STA)]
[NonParallelizable]
public class NodePastePopupTests
{
    [TestCase(Key.Enter, true)]
    [TestCase(Key.Escape, false)]
    public void RadioBindingsAndKeyboardCompletionUsePopupContract(Key key, bool accepted)
    {
        new VolumeWheelTests().SetUp();
        bool? completed = null;
        var vm = new NodePastePopupVM(); var popup = new NodePastePopup(vm, result => completed = result);
        var content = (StackPanel)popup.Content;
        UiTestPump.Layout(content, 420, 260);
        var radios = content.Children.OfType<RadioButton>().ToArray();
        Assert.That(radios.Length, Is.EqualTo(2));
        Assert.That(radios[0].GroupName, Is.EqualTo(radios[1].GroupName));
        Assert.That(radios[1].IsChecked, Is.True);
        foreach (var radio in radios)
        {
            Assert.That(radio.Style, Is.SameAs(popup.FindResource("OHsNodePasteTimingChoiceStyle")));
            Assert.That(radio.IsTabStop, Is.True);
        }
        radios[0].IsChecked = true;
        popup.Dispatcher.Invoke(() => { }, DispatcherPriority.DataBind);
        Assert.That(vm.Timing, Is.EqualTo(NodePasteTiming.SourceTimestamps));
        Assert.That(radios[1].IsChecked, Is.False);
        Assert.That(popup.HandlePasteKey(key, ModifierKeys.None), Is.True);
        Assert.That(completed, Is.EqualTo(accepted));
        Assert.That(popup.Result, Is.EqualTo(accepted ? NodePasteTiming.SourceTimestamps : (NodePasteTiming?)null));
    }
}
