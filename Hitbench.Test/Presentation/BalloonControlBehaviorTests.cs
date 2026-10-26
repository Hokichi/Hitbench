using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Hitbench.Resources.CustomControls;

namespace Hitbench.Test.Presentation;

[Apartment(ApartmentState.STA), NonParallelizable]
public sealed class BalloonControlBehaviorTests
{
    [TestCase(FlowDirection.LeftToRight)]
    [TestCase(FlowDirection.RightToLeft)]
    public void MissingResolvedIconCollapsesIconContent(FlowDirection direction)
    {
        new SharedUiResourceIntegrationTests().SharedResources_LoadTogetherAndProvideReusableControlContracts();
        var button = new BalloonButton { ButtonText = "Action", ShouldShowText = true, FlowDirection = direction };
        button.Style = (Style)Application.Current.FindResource(typeof(BalloonButton));
        UiTestPump.Layout(button, 240, 80);
        var slot = (FrameworkElement)button.Template.FindName("PART_IconSlot", button);
        Assert.That(slot.Visibility, Is.EqualTo(Visibility.Collapsed));
        button.ButtonIcon = Application.Current.FindResource("Check");
        Assert.That(slot.Visibility, Is.EqualTo(Visibility.Visible));
        button.ButtonIcon = null;
        Assert.That(slot.Visibility, Is.EqualTo(Visibility.Collapsed));
    }

    [TestCase(FlowDirection.LeftToRight)]
    [TestCase(FlowDirection.RightToLeft)]
    public void EmptyOrHiddenTextCollapsesTextContent(FlowDirection direction)
    {
        new SharedUiResourceIntegrationTests().SharedResources_LoadTogetherAndProvideReusableControlContracts();
        var button = new BalloonCheckBox { ButtonIcon = Application.Current.FindResource("Check"), ShouldShowText = true,
            ButtonText = "Action", CheckedText = "", FlowDirection = direction };
        button.Style = (Style)Application.Current.FindResource(typeof(BalloonCheckBox));
        UiTestPump.Layout(button, 240, 80);
        var content = (FrameworkElement)button.Template.FindName("PART_TextContent", button);
        button.IsChecked = true;
        Assert.That(content.Visibility, Is.EqualTo(Visibility.Collapsed));
        button.IsChecked = false;
        Assert.That(content.Visibility, Is.EqualTo(Visibility.Visible));
        button.ShouldShowText = false;
        Assert.That(content.Visibility, Is.EqualTo(Visibility.Collapsed));
        button.ButtonText = null;
        button.ShouldShowText = true;
        Assert.That(content.Visibility, Is.EqualTo(Visibility.Collapsed));
    }

    [Test]
    public void ActiveTogglePresentationUsesResolvedContent()
    {
        new SharedUiResourceIntegrationTests().SharedResources_LoadTogetherAndProvideReusableControlContracts();
        var state = new BalloonToggleState { ButtonText = "", ButtonIcon = Application.Current.FindResource("Check") };
        var toggle = new BalloonToggle { ButtonText = "Action", ShouldShowText = true,
            Style = (Style)Application.Current.FindResource(typeof(BalloonToggle)) };
        toggle.States.Add(state);
        UiTestPump.Layout(toggle, 240, 80);
        var icon = (FrameworkElement)toggle.Template.FindName("PART_IconSlot", toggle);
        var text = (FrameworkElement)toggle.Template.FindName("PART_TextContent", toggle);
        Assert.That(icon.Visibility, Is.EqualTo(Visibility.Collapsed));
        Assert.That(text.Visibility, Is.EqualTo(Visibility.Visible));
        toggle.SelectState(state);
        Assert.That(icon.Visibility, Is.EqualTo(Visibility.Visible));
        Assert.That(text.Visibility, Is.EqualTo(Visibility.Collapsed));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void HasIconDefaultsTrueAndCanChangeAtRuntime(bool showText)
    {
        new SharedUiResourceIntegrationTests().SharedResources_LoadTogetherAndProvideReusableControlContracts();
        var button = new BalloonButton { ButtonIcon = Application.Current.FindResource("Check"), ButtonText = "Action", SubText = "F2", ShouldShowText = showText };
        button.Style = (Style)Application.Current.FindResource(typeof(BalloonButton));
        UiTestPump.Layout(button, 200, 80);
        var property = DependencyPropertyDescriptor.FromName("HasIcon", typeof(BalloonControl), typeof(BalloonControl));
        Assert.That(property, Is.Not.Null, "BalloonControl must expose the HasIcon dependency property.");
        Assert.That(button.GetValue(property!.DependencyProperty), Is.True);
        var iconSlot = (FrameworkElement)button.Template.FindName("PART_IconSlot", button);
        button.SetValue(property.DependencyProperty, false);
        Assert.That(iconSlot.Visibility, Is.EqualTo(Visibility.Collapsed));
        button.SetValue(property.DependencyProperty, true);
        Assert.That(iconSlot.Visibility, Is.EqualTo(Visibility.Visible));
        Assert.That(button.ButtonText, Is.EqualTo("Action"));
    }

    [Test]
    public void HiddenIconRestoresPrecedenceAndDerivedState()
    {
        new SharedUiResourceIntegrationTests().SharedResources_LoadTogetherAndProvideReusableControlContracts();
        var button = new BalloonCheckBox { ButtonIcon = Application.Current.FindResource("PlusSolid"), CheckedIcon = Application.Current.FindResource("Check") };
        button.Style = (Style)Application.Current.FindResource(typeof(BalloonCheckBox));
        UiTestPump.Layout(button, 200, 80);
        var property = DependencyPropertyDescriptor.FromName("HasIcon", typeof(BalloonControl), typeof(BalloonControl));
        Assert.That(property, Is.Not.Null);
        button.SetValue(property!.DependencyProperty, false);
        button.IsChecked = true;
        button.SetValue(property.DependencyProperty, true);
        var icon = (System.Windows.Shapes.Path)button.Template.FindName("PART_Icon", button);
        Assert.That(icon.Data, Is.SameAs(button.CheckedIcon));
        Assert.That(button.IsChecked, Is.True);
    }

    [Test]
    public void PresentationChangesPreserveCallerWidthBinding()
    {
        var source = new Border { Width = 140 };
        var button = new BalloonButton { ButtonText = "Action" };
        button.SetBinding(FrameworkElement.WidthProperty, new Binding(nameof(FrameworkElement.Width)) { Source = source, Mode = BindingMode.OneWay });
        var expression = BindingOperations.GetBindingExpression(button, FrameworkElement.WidthProperty);
        button.ShouldShowText = true;
        button.ShouldShowText = false;
        Assert.That(BindingOperations.GetBindingExpression(button, FrameworkElement.WidthProperty), Is.SameAs(expression));
        Assert.That(button.Width, Is.EqualTo(140), "Presentation changes must not replace a caller's explicit width value.");
        source.Width = 180;
        Assert.That(button.Width, Is.EqualTo(180));
    }

    [Test]
    public void IconPrecedenceAndCommandParameterRemainStable()
    {
        var primary = new object(); var fallback = new object(); var parameter = new object();
        object? received = null;
        var button = new StubBalloonButton { Icon = primary, ButtonIcon = fallback, ButtonText = "Keep text",
            Command = new RelayCommand<object>(value => received = value), CommandParameter = parameter };
        Assert.That(button.CurrentIcon, Is.SameAs(primary));
        button.Icon = null;
        Assert.That(button.CurrentIcon, Is.SameAs(fallback));
        button.Invoke();
        Assert.That(received, Is.SameAs(parameter));
        Assert.That(button.CurrentText, Is.EqualTo("Keep text"));
        button.Command = new RelayCommand(() => received = null, () => false);
        button.Invoke();
        Assert.That(received, Is.SameAs(parameter));
    }

    [Test]
    public void RadioAndToggleSelectionKeepTheirNativeStateContracts()
    {
        var first = new BalloonRadioButton { GroupName = "choice" };
        var second = new BalloonRadioButton { GroupName = "choice" };
        var host = new StackPanel { Children = { first, second } };
        first.IsChecked = true; second.IsChecked = true;
        Assert.That(first.IsChecked, Is.False);
        Assert.That(second.IsChecked, Is.True);
        var called = 0;
        var accepted = new BalloonToggleState { OnChecked = new RelayCommand(() => called++) };
        var rejected = new BalloonToggleState { OnChecked = new RelayCommand(() => called++, () => false) };
        var toggle = new BalloonToggle(); toggle.States.Add(accepted); toggle.States.Add(rejected);
        toggle.SelectState(rejected);
        Assert.That(toggle.IsCycling, Is.False);
        toggle.SelectState(accepted);
        Assert.That(toggle.IsCycling, Is.True);
        Assert.That(called, Is.EqualTo(1));
        GC.KeepAlive(host);
    }

    [TestCase("Dark", "OHsBalloonNavInfoActionStyle")]
    [TestCase("Light", "OHsBalloonNavInfoActionStyle")]
    [TestCase("Dark", "OHsBalloonSurfaceActionStyle")]
    [TestCase("Light", "OHsBalloonSurfaceActionStyle")]
    [TestCase("Dark", "OHsBalloonDestructiveSolidActionStyle")]
    [TestCase("Light", "OHsBalloonDestructiveSolidActionStyle")]
    [TestCase("Dark", "OHsBalloonStateItemStyle")]
    [TestCase("Light", "OHsBalloonStateItemStyle")]
    public void SemanticActionsAssignTheCanonicalControlContract(string theme, string key)
    {
        new SharedUiResourceIntegrationTests().SharedResources_LoadTogetherAndProvideReusableControlContracts();
        var resources = Application.Current.Resources;
        resources.MergedDictionaries[0] = new ResourceDictionary
        {
            Source = new Uri($"/Hitbench;component/Resources/Themes/{theme}.xaml", UriKind.Relative)
        };
        var button = new BalloonButton { DataContext = new BalloonToggleState(), Style = (Style)resources[key], ButtonText = "Action", ShouldShowText = true,
            ButtonIcon = resources["Check"] };
        UiControlContract.AssertStyle<BalloonButton>(button, key);
        UiControlContract.AssertTemplate(button, "BalloonControlTemplate");
        Assert.That(button.ButtonText, Is.EqualTo("Action"));
    }
}
