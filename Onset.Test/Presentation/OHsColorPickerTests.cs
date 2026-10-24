using System.Windows;
using System.Windows.Input;
using System.Windows.Automation.Provider;
using Onset.Resources.CustomControls;
namespace Onset.Test.Presentation;
[Apartment(ApartmentState.STA)]
public class OHsColorPickerTests
{
    [Test] public void KeyboardAdjustsPlaneAndInvalidCoordinatesDoNothing()
    {
        var picker=new OHsColorPicker();UiTestPump.Layout(picker,200,100);
        using var source=new InMemoryPresentationSource();
        picker.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice,source,0,Key.Left){RoutedEvent=Keyboard.KeyDownEvent});
        Assert.That(picker.SelectedColor,Is.EqualTo("#FF0303"));
        var old=picker.SelectedColor;picker.SelectAt(new Point(double.NaN,0));Assert.That(picker.SelectedColor,Is.EqualTo(old));
    }
    [Test] public void CoordinatesAndHueChangeSelectedRgb()
    {
        var picker=new OHsColorPicker(); UiTestPump.Layout(picker,200,100);
        picker.SelectAt(new Point(200,0)); Assert.That(picker.SelectedColor,Is.EqualTo("#FF0000"));
        picker.Hue=120; Assert.That(picker.SelectedColor,Is.EqualTo("#00FF00"));
        picker.SelectAt(new Point(0,100)); Assert.That(picker.SelectedColor,Is.EqualTo("#000000"));
        picker.SelectAt(new Point(200,0)); Assert.That(picker.SelectedColor,Is.EqualTo("#00FF00"));
        picker.FlowDirection=FlowDirection.RightToLeft; picker.SelectAt(new Point(200,0));
        Assert.That(picker.SelectedColor,Is.EqualTo("#FFFFFF"));
    }
    [Test] public void AutomationAndDisabledInputHonorContract()
    {
        var picker=new OHsColorPicker(); UiTestPump.Layout(picker,200,100);
        var peer=new OHsColorPickerAutomationPeer(picker); var value=(IValueProvider)peer;
        value.SetValue("#abcdef"); Assert.That(value.Value,Is.EqualTo("#ABCDEF"));
        Assert.Throws<ArgumentException>(()=>value.SetValue("bad"));
        picker.IsEnabled=false; picker.SelectAt(new Point(0,0)); Assert.That(value.Value,Is.EqualTo("#ABCDEF"));
        Assert.That(value.IsReadOnly,Is.True);
    }
}
