using System.Windows.Controls;
using System.Windows.Input;
using Soundie.Resources.CustomControls;
using Soundie.ViewModels.Popups;
using Soundie.Views.Popups;
namespace Soundie.Test.Presentation;
[Apartment(ApartmentState.STA), NonParallelizable]
public class OHsTrackAppearancePopupIntegrationTests
{
    [Test]
    public void HueMarkerUsesNativeInputAndUpdatesDraft()
    {
        new VolumeWheelTests().SetUp();
        var vm = new OHsTrackAppearancePopupVM(new("Old", null, 0), "#FF0000");
        var popup = new OHsTrackAppearancePopup(vm, _ => { });
        using var view = new InMemoryView(popup, scheduleLayout: false);
        var slider = (ValueSlider)popup.FindName("HueInput");
        Assert.That(slider, Is.Not.Null);
        Assert.That(slider.Style, Is.SameAs(popup.FindResource("OHsTrackAppearanceHueStyle")));
        Assert.That(slider.Template, Is.SameAs(popup.FindResource("OHsTrackAppearanceHueTemplate")));
        Assert.That(System.Windows.Automation.AutomationProperties.GetName(slider), Is.EqualTo("Hue"));
        using var source = new InMemoryPresentationSource();
        slider.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, source, 0, Key.Right) { RoutedEvent = Keyboard.KeyDownEvent });
        Assert.That(slider.Value, Is.EqualTo(1));
        Slider.IncreaseLarge.Execute(null, slider);
        Assert.That(slider.Value, Is.EqualTo(16));
        var peer = System.Windows.Automation.Peers.UIElementAutomationPeer.CreatePeerForElement(slider)!;
        var range = (System.Windows.Automation.Provider.IRangeValueProvider)peer.GetPattern(System.Windows.Automation.Peers.PatternInterface.RangeValue)!;
        Assert.That((range.Minimum, range.Maximum, range.SmallChange, range.LargeChange), Is.EqualTo((0d, 360d, 1d, 15d)));
        range.SetValue(120);
        Assert.That(vm.Color, Is.EqualTo("#00FF00"));
        vm.Color = "#0000FF"; view.UpdateLayout();
        Assert.That(slider.Value, Is.EqualTo(240));
        Slider.MaximizeValue.Execute(null, slider); Assert.That(slider.Value, Is.EqualTo(360));
        Slider.MinimizeValue.Execute(null, slider); Assert.That(slider.Value, Is.Zero);
        slider.IsEnabled = false;
        Assert.Throws<System.Windows.Automation.ElementNotEnabledException>(() => range.SetValue(120));
        Assert.That(slider.Value, Is.Zero);
    }

    [TestCase(200, System.Windows.FlowDirection.LeftToRight)]
    [TestCase(360, System.Windows.FlowDirection.LeftToRight)]
    [TestCase(200, System.Windows.FlowDirection.RightToLeft)]
    [TestCase(360, System.Windows.FlowDirection.RightToLeft)]
    public void HueTrackMapsExplicitInputDirection(double width, System.Windows.FlowDirection direction)
    {
        new VolumeWheelTests().SetUp();
        var popup = new OHsTrackAppearancePopup(new(new("Old", null, 0), "#FF0000"), _ => { });
        var slider = new ValueSlider { Style = (System.Windows.Style)popup.FindResource("OHsTrackAppearanceHueStyle"),
            Minimum = 0, Maximum = 360, FlowDirection = direction };
        var host = new Border { Child = slider, FlowDirection = System.Windows.FlowDirection.LeftToRight };
        UiTestPump.Layout(host, width, 40);
        var track = (System.Windows.Controls.Primitives.Track)slider.Template.FindName("PART_Track", slider);
        Assert.That(track, Is.Not.Null);
        Assert.That(slider.IsMoveToPointEnabled, Is.True);
        var left = track.ValueFromPoint(host.TranslatePoint(new System.Windows.Point(0, 20), track));
        var right = track.ValueFromPoint(host.TranslatePoint(new System.Windows.Point(width, 20), track));
        Assert.That(direction == System.Windows.FlowDirection.LeftToRight ? right > left : left > right, Is.True);
    }
    [Test] public void CleanEscapeAndExplicitCancelCompleteWithoutSaving()
    {
        new VolumeWheelTests().SetUp();bool? accepted=null;
        var vm=new OHsTrackAppearancePopupVM(new("Old",null,0),"#123456");
        var popup=new OHsTrackAppearancePopup(vm,v=>accepted=v);
        using var view=new InMemoryView(popup,scheduleLayout:false);
        popup.HandleKey(Key.Escape);Assert.That(accepted,Is.False);
        accepted=null;vm.Color="#ABCDEF";
        var cancel=((System.Windows.Controls.Panel)view.Root).Children.OfType<System.Windows.Controls.StackPanel>().Last().Children.OfType<BalloonButton>().First();
        cancel.RaiseEvent(new System.Windows.RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
        Assert.That(accepted,Is.False);
    }
    [Test] public void DraftBindingAndEscapeAreIntentional()
    {
        new VolumeWheelTests().SetUp(); bool? accepted=null;
        var vm=new OHsTrackAppearancePopupVM(new("Old",null,0),"#123456");
        var popup=new OHsTrackAppearancePopup(vm,v=>accepted=v);
        using var view=new InMemoryView(popup,scheduleLayout:false);
        var name=(TextBox)popup.FindName("NameInput");
        var picker=(OHsColorPicker)popup.FindName("ColorInput");
        Assert.That(name.Style,Is.SameAs(popup.FindResource("RoundedTextInputStyle")));
        Assert.That(picker.Style,Is.SameAs(popup.FindResource("OHsTrackAppearanceColorPickerStyle")));
        name.Text="New"; view.UpdateLayout(); Assert.That(vm.Name,Is.EqualTo("New"));
        name.RaiseEvent(new KeyboardFocusChangedEventArgs(Keyboard.PrimaryDevice, 0, name, null) { RoutedEvent = Keyboard.LostKeyboardFocusEvent });
        Assert.That(accepted, Is.Null, "Leaving the name field must not commit the popup draft.");
        Assert.That(popup.HandleKey(Key.Escape),Is.True); Assert.That(accepted,Is.Null);
        popup.HandleKey(Key.Enter); Assert.That(accepted,Is.True);
        Assert.That(vm.CreateResult().Color,Is.Null);
    }
}
