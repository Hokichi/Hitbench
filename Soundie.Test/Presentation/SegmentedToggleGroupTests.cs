using CommunityToolkit.Mvvm.Input;
using Soundie.Resources.CustomControls;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;

namespace Soundie.Test.Presentation;

[Apartment(ApartmentState.STA)]
[NonParallelizable]
public sealed class SegmentedToggleGroupTests
{
    private static (SegmentedToggleGroup Group, StubSegmentedToggleOption Select, StubSegmentedToggleOption Draw) Create()
    {
        var select = new StubSegmentedToggleOption { Content = "Select", Value = "Select" };
        var draw = new StubSegmentedToggleOption { Content = "Draw", Value = "Draw" };
        var group = new SegmentedToggleGroup { Items = { select, draw } };
        return (group, select, draw);
    }

    private static ISelectionItemProvider Selection(SegmentedToggleOption option) =>
        (ISelectionItemProvider)UIElementAutomationPeer.CreatePeerForElement(option)!.GetPattern(PatternInterface.SelectionItem);

    [TestCase("Pointer")]
    [TestCase("Enter")]
    [TestCase("Space")]
    [TestCase("Automation")]
    public void PointerAndKeyboardSelectionUpdateOnce(string activation)
    {
        var (group, select, draw) = Create();
        group.SelectedValue = "Select";
        var changes = 0;
        group.OptionSelected += (_, _) => changes++;
        if (activation == "Pointer") draw.Activate();
        else if (activation == "Automation") Selection(draw).Select();
        else draw.PressKey(activation == "Enter" ? Key.Enter : Key.Space);
        Assert.That(group.SelectedValue, Is.EqualTo("Draw"));
        Assert.That(select.IsSelected, Is.False);
        Assert.That(draw.IsSelected, Is.True);
        Assert.That(changes, Is.EqualTo(1));
        if (activation == "Automation")
        {
            Assert.That(Selection(draw).IsSelected, Is.True);
            Assert.That(UIElementAutomationPeer.CreatePeerForElement(draw)!.GetAutomationControlType(), Is.EqualTo(AutomationControlType.RadioButton));
            Assert.That(UIElementAutomationPeer.CreatePeerForElement(draw)!.GetName(), Is.EqualTo("Draw"));
        }
    }

    [Test]
    public void RepeatedSelectionDoesNotPublishAgain()
    {
        var (group, _, draw) = Create();
        var changes = 0;
        group.OptionSelected += (_, _) => changes++;
        draw.Activate();
        draw.Activate();
        group.SelectedValue = "Draw";
        Assert.That(changes, Is.EqualTo(1));
    }

    [Test]
    public void SelectedValueBindingSurvivesBothDirections()
    {
        var (group, select, draw) = Create();
        var host = new Border { Tag = "Select" };
        group.SetBinding(SegmentedToggleGroup.SelectedValueProperty, new Binding(nameof(FrameworkElement.Tag))
            { Source = host, Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged });
        var changes = 0;
        group.OptionSelected += (_, _) => changes++;
        draw.Activate();
        Assert.That(host.Tag, Is.EqualTo("Draw"));
        host.Tag = "Select";
        Assert.That(select.IsSelected, Is.True);
        Assert.That(draw.IsSelected, Is.False);
        Assert.That(BindingOperations.IsDataBound(group, SegmentedToggleGroup.SelectedValueProperty), Is.True);
        Assert.That(changes, Is.EqualTo(1), "External reconciliation is not a business action.");
    }

    [Test]
    public void SeparateGroupsKeepIndependentValues()
    {
        var (group, _, draw) = Create();
        var (other, otherSelect, _) = Create();
        group.SelectedValue = "Select";
        other.SelectedValue = "Select";
        draw.Activate();
        Assert.That(other.SelectedValue, Is.EqualTo("Select"));
        Assert.That(otherSelect.IsSelected, Is.True);
        Assert.That(group.SelectedValue, Is.EqualTo("Draw"));
    }

    [Test]
    public void NullUnknownAndRemovedValuesClearStaleFlags()
    {
        var (group, select, draw) = Create();
        group.SelectedValue = "Select";
        group.SelectedValue = null;
        Assert.That(select.IsSelected || draw.IsSelected, Is.False);
        group.SelectedValue = "missing";
        Assert.That(select.IsSelected || draw.IsSelected, Is.False);
        var matching = new SegmentedToggleOption { Content = "New", Value = "missing" };
        group.Items.Add(matching);
        Assert.That(matching.IsSelected, Is.True);
        group.Items.Remove(matching);
        Assert.That(select.IsSelected || draw.IsSelected, Is.False);
        Assert.That(group.SelectedValue, Is.EqualTo("missing"));
    }

    [Test]
    public void DisabledOptionsAndForeignClicksAreIgnored()
    {
        var (group, _, draw) = Create();
        group.SelectedValue = "Select";
        var changes = 0;
        group.OptionSelected += (_, _) => changes++;
        draw.IsEnabled = false;
        draw.Activate();
        draw.PressKey(Key.Enter);
        Assert.Throws<ElementNotEnabledException>(() => Selection(draw).Select());
        draw.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent, new Button()));
        Assert.That(group.SelectedValue, Is.EqualTo("Select"));
        Assert.That(changes, Is.Zero);
    }

    [TestCase(FlowDirection.LeftToRight, Key.Right)]
    [TestCase(FlowDirection.RightToLeft, Key.Left)]
    public void ArrowHomeEndRespectFlowAndSkipUnavailableOptions(FlowDirection flow, Key forward)
    {
        var (group, select, draw) = Create();
        var disabled = new SegmentedToggleOption { Value = "Disabled", IsEnabled = false };
        var collapsed = new SegmentedToggleOption { Value = "Collapsed", Visibility = Visibility.Collapsed };
        group.Items.Insert(1, disabled);
        group.Items.Insert(2, collapsed);
        group.FlowDirection = flow;
        group.SelectedValue = "Select";
        var changes = 0;
        group.OptionSelected += (_, _) => changes++;
        using var source = new InMemoryPresentationSource { RootVisual = group };
        void Press(UIElement origin, Key key) => origin.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, source, 0, key)
            { RoutedEvent = Keyboard.PreviewKeyDownEvent });
        Press(select, forward);
        Assert.That(group.SelectedValue, Is.EqualTo("Draw"));
        Press(draw, forward);
        Assert.That(group.SelectedValue, Is.EqualTo("Select"));
        Press(select, Key.End);
        Assert.That(group.SelectedValue, Is.EqualTo("Draw"));
        Press(draw, Key.Home);
        Assert.That(group.SelectedValue, Is.EqualTo("Select"));
        Assert.That(changes, Is.EqualTo(4));
        select.IsEnabled = false;
        draw.IsEnabled = false;
        Press(select, Key.End);
        Assert.That(changes, Is.EqualTo(4));
    }

    [Test]
    public void LegacyOptionPropertiesKeepTheirContract()
    {
        var executions = 0;
        var option = new StubSegmentedToggleOption { SelectOnClick = false, CommandParameter = "Draw",
            Command = new RelayCommand<string>(value => { Assert.That(value, Is.EqualTo("Draw")); executions++; }) };
        option.Activate();
        Assert.That(option.IsSelected, Is.False);
        Assert.That(executions, Is.EqualTo(1));
        var group = new SegmentedToggleGroup { Items = { option } };
        option.SelectOnClick = true;
        option.Activate();
        Assert.That(group.SelectedValue, Is.EqualTo("Draw"));
        Assert.That(executions, Is.EqualTo(2));
    }

    [Test]
    public void OptionSelectionBindingSurvivesNativeSynchronization()
    {
        var host = new Border { Tag = false };
        var option = new StubSegmentedToggleOption();
        option.SetBinding(SegmentedToggleOption.IsSelectedProperty, new Binding(nameof(FrameworkElement.Tag))
            { Source = host, Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged });
        option.Activate();
        Assert.That(host.Tag, Is.True);
        host.Tag = false;
        Assert.That(option.IsSelected, Is.False);
        Assert.That(Selection(option).IsSelected, Is.False);
        Assert.That(BindingOperations.IsDataBound(option, SegmentedToggleOption.IsSelectedProperty), Is.True);
    }
}
