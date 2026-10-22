using System.Windows.Controls;
using System.Windows.Input;
using Soundie.Resources.CustomControls;
using Soundie.ViewModels.Popups;
using Soundie.Views.Popups;
namespace Soundie.Test.Presentation;
[Apartment(ApartmentState.STA), NonParallelizable]
public class OHsTrackAppearancePopupIntegrationTests
{
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
        Assert.That(popup.HandleKey(Key.Escape),Is.True); Assert.That(accepted,Is.Null);
        popup.HandleKey(Key.Enter); Assert.That(accepted,Is.True);
        Assert.That(vm.CreateResult().Color,Is.Null);
    }
}
