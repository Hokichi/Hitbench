using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using Onset.Resources.CustomControls;
using Onset.Resources.DataModels;

namespace Onset.Test.Presentation;

[Apartment(ApartmentState.STA), NonParallelizable]
public class NumericUpDownDragTests
{
    private static StubDragNumericUpDown Create(decimal value = 50)
    {
        new SharedUiResourceIntegrationTests().SharedResources_LoadTogetherAndProvideReusableControlContracts();
        var control = new StubDragNumericUpDown { Style = (Style)Application.Current.FindResource(typeof(NumericUpDown)),
            Value = value, LowerLimit = 5, UpperLimit = 100, Step = 1, IsIntegerOnly = true,
            ClampIntegerToRange = true, IsVerticalDragEnabled = true };
        UiTestPump.Layout(control, 180, 40);
        return control;
    }
    private static TextBox Editor(NumericUpDown control) => (TextBox)control.Template.FindName("PART_TextBox", control);

    [Test]
    public void DragUpdatesBoundVolumeInBothDirectionsAndRetainsRemainder()
    {
        var row = new StubNumericValue { Volume = 50 };
        var control = Create(); control.DataContext = row;
        BindingOperations.SetBinding(control, NumericUpDown.ValueProperty, new Binding("Volume") { Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged });
        control.BeginVerticalDrag(100, 1, false);
        Assert.That(control.ContinueVerticalDrag(99, true), Is.False);
        Assert.That(control.Captures, Is.Zero); Assert.That(row.Volume, Is.EqualTo(50));
        Assert.That(control.ContinueVerticalDrag(92, true), Is.True);
        Assert.That(row.Volume, Is.EqualTo(52)); Assert.That(control.IsVerticalDragActive, Is.True);
        control.ContinueVerticalDrag(90, true); Assert.That(row.Volume, Is.EqualTo(52));
        control.ContinueVerticalDrag(88, true); Assert.That(row.Volume, Is.EqualTo(53));
        control.ContinueVerticalDrag(92, true); Assert.That(row.Volume, Is.EqualTo(52));
        Assert.That(BindingOperations.IsDataBound(control, NumericUpDown.ValueProperty), Is.True);
        control.CancelVerticalDrag(); Assert.That(control.Releases, Is.EqualTo(1));
    }
    [TestCase(99, 80, 84, 100, 99)]
    [TestCase(6, 120, 116, 5, 6)]
    public void OvershootClampsAndReversalRespondsImmediately(int start, int overshootY, int reverseY, int anchor, int reversed)
    {
        var control = Create(start); control.BeginVerticalDrag(100, 1, false);
        control.ContinueVerticalDrag(overshootY, true); Assert.That(control.Value, Is.EqualTo(anchor));
        control.ContinueVerticalDrag(reverseY, true); Assert.That(control.Value, Is.EqualTo(reversed));
        control.CancelVerticalDrag();
    }
    [TestCase("arrow")][TestCase("double")][TestCase("disabled")][TestCase("optout")][TestCase("capture")]
    public void IneligiblePressOrFailedCaptureCannotAdjustVolume(string reason)
    {
        var control = Create();
        if (reason == "disabled") control.IsEnabled = false;
        if (reason == "optout") control.IsVerticalDragEnabled = false;
        if (reason == "capture") control.CanCapture = false;
        control.BeginVerticalDrag(100, reason == "double" ? 2 : 1, reason == "arrow");
        Assert.That(control.ContinueVerticalDrag(80, true), Is.False);
        Assert.That(control.Value, Is.EqualTo(50)); Assert.That(control.IsVerticalDragActive, Is.False);
    }
    [TestCase("release")][TestCase("lost")][TestCase("cancel")][TestCase("disabled")]
    [TestCase("context")][TestCase("template")][TestCase("unload")][TestCase("deactivate")][TestCase("optout")]
    public void GestureLifetimeCannotLeakIntoLaterMovement(string reason)
    {
        var control = Create();
        var owner = new StubDragOwnerWindow { Content = control };
        try
        {
            control.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
            control.BeginVerticalDrag(100, 1, false); control.ContinueVerticalDrag(92, true);
            Assert.That(control.Value, Is.EqualTo(52));
            switch (reason)
            {
                case "release": control.ContinueVerticalDrag(92, false); break;
                case "lost": control.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, 0) { RoutedEvent = Mouse.LostMouseCaptureEvent }); break;
                case "cancel": control.CancelVerticalDrag(); break;
                case "disabled": control.IsEnabled = false; break;
                case "context": control.DataContext = new StubNumericValue { Volume = 52 }; break;
                case "template": control.OnApplyTemplate(); break;
                case "unload": control.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent)); break;
                case "deactivate": owner.NotifyDeactivated(); break;
                case "optout": control.IsVerticalDragEnabled = false; break;
            }
            Assert.That(control.IsVerticalDragActive, Is.False);
            control.ContinueVerticalDrag(60, true); Assert.That(control.Value, Is.EqualTo(52));
        }
        finally { control.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent)); owner.Content = null; owner.Close(); }
    }
    [TestCase("120", 100)][TestCase("0", 5)]
    public void LostFocusClampsWholeDraftToAnchor(string draft, int expected)
    {
        var control = Create(); var editor = Editor(control); editor.Text = draft;
        Assert.That(control.Value, Is.EqualTo(50));
        editor.RaiseEvent(new KeyboardFocusChangedEventArgs(Keyboard.PrimaryDevice, 0, editor, null) { RoutedEvent = Keyboard.LostKeyboardFocusEvent });
        Assert.That(control.Value, Is.EqualTo(expected)); Assert.That(control.InputError, Is.Null);
    }
    [TestCase("commit", 100)][TestCase("enter", 100)][TestCase("spinner", 99)][TestCase("drag", 98)][TestCase("wheel", 99)]
    public void EveryCommitPathUsesTheSameClamp(string path, int expected)
    {
        var control = Create(); Editor(control).Text = "120";
        if (path == "commit") control.CommitInput();
        if (path == "enter")
        {
            using var source = new InMemoryPresentationSource();
            control.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, source, 0, Key.Enter) { RoutedEvent = Keyboard.PreviewKeyDownEvent });
        }
        if (path == "spinner") ((RepeatButton)control.Template.FindName("PART_DownButton", control)).RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        if (path == "wheel")
        {
            Assert.That(control.HandleMouseWheel(-120, false), Is.False);
            Assert.That(control.Value, Is.EqualTo(50));
            Assert.That(control.HandleMouseWheel(-120, true), Is.True);
        }
        if (path == "drag") { control.BeginVerticalDrag(100, 1, false); control.ContinueVerticalDrag(108, true); control.CancelVerticalDrag(); }
        Assert.That(control.Value, Is.EqualTo(expected));
    }
    [TestCase("")][TestCase("bad")][TestCase("50.5")]
    public void ClampingDoesNotRoundOrAcceptInvalidDrafts(string draft)
    {
        var control = Create(); Editor(control).Text = draft;
        Assert.That(control.CommitInput(), Is.False); Assert.That(control.Value, Is.EqualTo(50));
        control.BeginVerticalDrag(100, 1, false); control.ContinueVerticalDrag(80, true);
        Assert.That(control.Value, Is.EqualTo(50)); Assert.That(control.InputError, Is.Not.Null);
    }
    [TestCase("en-US", "5,5")][TestCase("de-DE", "5.5")]
    public void ClampingRetainsCultureSensitiveFractionalValidation(string culture, string draft)
    {
        var previous = CultureInfo.CurrentCulture;
        try { CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture); var control = Create(); Editor(control).Text = draft;
            Assert.That(control.CommitInput(), Is.False); Assert.That(control.Value, Is.EqualTo(50)); }
        finally { CultureInfo.CurrentCulture = previous; }
    }
    [Test]
    public void PressDoesNotConsumeOrdinaryEditorClick()
    {
        var control = Create(); var editor = Editor(control); editor.Select(0, 1);
        var press = new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left) { RoutedEvent = UIElement.PreviewMouseLeftButtonDownEvent };
        control.RaiseEvent(press);
        Assert.That(press.Handled, Is.False); Assert.That(editor.SelectionLength, Is.EqualTo(1)); Assert.That(control.Captures, Is.Zero);
    }
}
