using System.Windows.Controls;
using System.Windows.Input;
using Hitbench.Resources.CustomControls;
using Hitbench.ViewModels.Popups;
using Hitbench.Views.Popups;
namespace Hitbench.Test.Presentation;
[Apartment(ApartmentState.STA), NonParallelizable]
public class OHsTrackAppearancePopupIntegrationTests
{
    [Test]
    public void SaveCompletesCombinedDraftOnlyOnce()
    {
        new VolumeWheelTests().SetUp();
        var vm = new OHsTrackAppearancePopupVM(new(null, 0) { Name = "Old" }, "#FFFFFF");
        var completions = 0;
        var popup = new OHsTrackAppearancePopup(vm, accepted => { Assert.That(accepted, Is.True); completions++; });
        using var view = new InMemoryView(popup, scheduleLayout: false);
        ((TextBox)popup.FindName("NameInput")).Text = "New";
        popup.HandleKey(Key.Escape);
        Assert.That(completions, Is.Zero);
        popup.HandleKey(Key.Enter); popup.HandleKey(Key.Enter);
        Assert.That(completions, Is.EqualTo(1));
        Assert.That(vm.CreateResult().Name, Is.EqualTo("New"));
        Assert.That(vm.CreateResult().Color, Is.Null);
    }

    [Test]
    public void AppearanceActionsOptIntoTabNavigation()
    {
        new VolumeWheelTests().SetUp();
        var popup = new OHsTrackAppearancePopup(new(new(null, 0), "#123456"), _ => { });
        using var view = new InMemoryView(popup, scheduleLayout: false);
        view.Root.Resources.MergedDictionaries.Add(popup.Resources);
        foreach (var name in new[] { "Cancel", "Save" })
        {
            var action = Action(view.Root, name);
            Assert.That(action.IsTabStop, Is.True, name);
            Assert.That(popup.FindName($"{name}Action"), Is.SameAs(action));
            UiControlContract.AssertStyle<BalloonButton>(action, $"OHsTrackAppearance{name}ActionStyle");
            Assert.That(action.FocusVisualStyle, Is.SameAs(action.FindResource("DefaultFocusVisualStyle")));
        }
    }

    [Test]
    public void EnterOnCancelDiscardsDirtyDraft()
    {
        new VolumeWheelTests().SetUp();
        bool? accepted = null;
        var vm = new OHsTrackAppearancePopupVM(new(null, 0), "#123456") { Color = "#ABCDEF" };
        var popup = new OHsTrackAppearancePopup(vm, value => accepted = value);
        using var view = new InMemoryView(popup, scheduleLayout: false);
        popup.HandleKey(Key.Enter, Action(view.Root, "Cancel"));
        Assert.That(accepted, Is.False);
        Assert.That(vm.IsDirty, Is.True);
    }

    [Test]
    public void SpaceOnCancelDiscardsDirtyDraft()
    {
        new VolumeWheelTests().SetUp();
        bool? accepted = null;
        var vm = new OHsTrackAppearancePopupVM(new(null, 0), "#123456") { Color = "#ABCDEF" };
        var popup = new OHsTrackAppearancePopup(vm, value => accepted = value);
        using var view = new InMemoryView(popup, scheduleLayout: false);
        using var source = new InMemoryPresentationSource();
        var action = Action(view.Root, "Cancel");
        action.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, source, 0, Key.Space) { RoutedEvent = Keyboard.KeyDownEvent });
        action.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, source, 0, Key.Space) { RoutedEvent = Keyboard.KeyUpEvent });
        Assert.That(accepted, Is.False);
    }

    [Test]
    public void InvalidSaveCannotCommit()
    {
        new VolumeWheelTests().SetUp();
        bool? accepted = null;
        var vm = new OHsTrackAppearancePopupVM(new(null, 0), "#123456") { Color = "invalid" };
        var popup = new OHsTrackAppearancePopup(vm, value => accepted = value);
        using var view = new InMemoryView(popup, scheduleLayout: false);
        var save = Action(view.Root, "Save");
        Assert.That(save.IsEnabled, Is.False);
        popup.HandleKey(Key.Enter, save);
        Assert.That(accepted, Is.Null);
    }

    private static BalloonButton Action(System.Windows.DependencyObject root, string name) =>
        Descendants(root).OfType<BalloonButton>().Single(button =>
            System.Windows.Automation.AutomationProperties.GetName(button) == name);

    [TestCase(1920, 1080, System.Windows.FlowDirection.LeftToRight)]
    [TestCase(640, 480, System.Windows.FlowDirection.LeftToRight)]
    [TestCase(480, 320, System.Windows.FlowDirection.LeftToRight)]
    [TestCase(1920, 1080, System.Windows.FlowDirection.RightToLeft)]
    [TestCase(640, 480, System.Windows.FlowDirection.RightToLeft)]
    [TestCase(480, 320, System.Windows.FlowDirection.RightToLeft)]
    public void ShortWorkAreaKeepsContentNavigable(double width, double height, System.Windows.FlowDirection direction)
    {
        new VolumeWheelTests().SetUp();
        bool? accepted = null;
        var vm = new OHsTrackAppearancePopupVM(new(null, 0), "#123456") { Color = "#ABCDEF" };
        var popup = new OHsTrackAppearancePopup(vm, value => accepted = value) { FlowDirection = direction };
        const double suppliedChrome = 96;
        var content = (System.Windows.FrameworkElement)popup.Content;
        content.Measure(new System.Windows.Size(Math.Min(popup.Width, width), double.PositiveInfinity));
        var naturalHeight = content.DesiredSize.Height;
        TestContext.Out.WriteLine($"Natural content extent {naturalHeight}; supplied work area {width} x {height}, chrome {suppliedChrome}.");
        popup.ApplyWorkArea(new System.Windows.Size(width, height), suppliedChrome);
        content.Measure(new System.Windows.Size(Math.Min(popup.Width, width), double.PositiveInfinity));
        Assert.That(content.DesiredSize.Height, Is.LessThanOrEqualTo(height - suppliedChrome));
        if (naturalHeight > height - suppliedChrome)
        {
            var scroll = popup.FindName("AppearanceScroll") as ScrollViewer;
            Assert.That(scroll, Is.Not.Null, "Overflowing content requires a reachable scrolling viewport.");
            UiTestPump.Layout(scroll!, Math.Min(popup.Width, width), scroll!.MaxHeight);
            scroll.ScrollToBottom();
            scroll.UpdateLayout();
            Assert.That(scroll.VerticalOffset, Is.EqualTo(scroll.ScrollableHeight));
            Assert.That(vm.Color, Is.EqualTo("#ABCDEF"));
        }
        popup.HandleKey(Key.Enter, popup.FindName("CancelAction") as System.Windows.DependencyObject);
        Assert.That(accepted, Is.False);
    }

    [Test]
    public void HueMarkerUsesNativeInputAndUpdatesDraft()
    {
        new VolumeWheelTests().SetUp();
        var vm = new OHsTrackAppearancePopupVM(new(null, 0), "#FF0000");
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
        var popup = new OHsTrackAppearancePopup(new(new(null, 0), "#FF0000"), _ => { });
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
        var vm=new OHsTrackAppearancePopupVM(new(null, 0),"#123456");
        var popup=new OHsTrackAppearancePopup(vm,v=>accepted=v);
        using var view=new InMemoryView(popup,scheduleLayout:false);
        popup.HandleKey(Key.Escape);Assert.That(accepted,Is.False);
        accepted=null;
        popup=new OHsTrackAppearancePopup(vm,v=>accepted=v);
        using var secondView=new InMemoryView(popup,scheduleLayout:false);
        vm.Color="#ABCDEF";
        var cancel = Descendants(secondView.Root).OfType<BalloonButton>().Single(button =>
            System.Windows.Automation.AutomationProperties.GetName(button) == "Cancel");
        cancel.RaiseEvent(new System.Windows.RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
        Assert.That(accepted,Is.False);
    }
    [Test] public void DraftBindingAndEscapeAreIntentional()
    {
        new VolumeWheelTests().SetUp(); bool? accepted=null;
        var vm=new OHsTrackAppearancePopupVM(new(null, 0),"#123456");
        var popup=new OHsTrackAppearancePopup(vm,v=>accepted=v);
        using var view=new InMemoryView(popup,scheduleLayout:false);
        var picker=(OHsColorPicker)popup.FindName("ColorInput");
        view.Root.Resources.MergedDictionaries.Add(popup.Resources);
        var name = (TextBox)popup.FindName("NameInput");
        UiControlContract.AssertStyle<TextBox>(name, "OHsTrackAppearanceNameInputStyle");
        name.Text = "Edited";
        Assert.That(vm.Name, Is.EqualTo("Edited"));
        Assert.That(picker.Style,Is.SameAs(popup.FindResource("OHsTrackAppearanceColorPickerStyle")));
        picker.SelectedColor="#ABCDEF"; view.UpdateLayout(); Assert.That(vm.Color,Is.EqualTo("#ABCDEF"));
        picker.RaiseEvent(new KeyboardFocusChangedEventArgs(Keyboard.PrimaryDevice, 0, picker, null) { RoutedEvent = Keyboard.LostKeyboardFocusEvent });
        Assert.That(accepted, Is.Null, "Leaving the picker must not commit the popup draft.");
        Assert.That(popup.HandleKey(Key.Escape),Is.True); Assert.That(accepted,Is.Null);
        popup.HandleKey(Key.Enter); Assert.That(accepted,Is.True);
        Assert.That(vm.CreateResult().Color,Is.EqualTo("#ABCDEF"));
    }

    private static IEnumerable<System.Windows.DependencyObject> Descendants(System.Windows.DependencyObject owner)
    {
        foreach (var child in Hitbench.Resources.Helpers.DependencyObjectTree.GetChildren(owner))
        {
            yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }
}
