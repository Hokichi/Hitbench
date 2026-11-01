using CommunityToolkit.Mvvm.Input;
using Hitbench.Resources.CustomControls;
using System.Windows;
using System.Windows.Data;

namespace Hitbench.Test.Presentation;

[Apartment(ApartmentState.STA), NonParallelizable]
public sealed class BalloonMenuTests
{
    [TestCase(typeof(BalloonMenu))]
    [TestCase(typeof(BalloonMenuOption))]
    public void TextCannotBeEnabled(Type type)
    {
        var control = (BalloonButton)Activator.CreateInstance(type)!;
        Assert.That(control.ShouldShowText, Is.False);
        control.ShouldShowText = true;
        Assert.That(control.ShouldShowText, Is.False);
        control.ClearValue(BalloonControl.ShouldShowTextProperty);
        control.Style = new Style(type)
        {
            Setters = { new Setter(BalloonControl.ShouldShowTextProperty, true) },
            Triggers = { new Trigger { Property = UIElement.IsEnabledProperty, Value = true,
                Setters = { new Setter(BalloonControl.ShouldShowTextProperty, true) } } }
        };
        Assert.That(control.ShouldShowText, Is.False);
        var source = new BalloonButton();
        control.SetBinding(BalloonControl.ShouldShowTextProperty,
            new Binding(nameof(BalloonControl.ShouldShowText)) { Source = source, Mode = BindingMode.OneWay });
        source.ShouldShowText = true;
        Assert.That(control.ShouldShowText, Is.False);
        control.SetCurrentValue(BalloonControl.ShouldShowTextProperty, true);
        Assert.That(control.ShouldShowText, Is.False);
    }

    [Test]
    public void OptionsAreInstanceOwned()
    {
        var first = new BalloonMenu();
        var second = new BalloonMenu();
        var parameter = new object();
        var command = new RelayCommand<object>(_ => { });
        var option = new BalloonMenuOption { Command = command, CommandParameter = parameter };
        first.Options.Add(option);
        Assert.That(second.Options, Is.Empty);
        Assert.That(option.Command, Is.SameAs(command));
        Assert.That(option.CommandParameter, Is.SameAs(parameter));
        Assert.That(first.IsExpanded, Is.False);
    }

    [TestCase(typeof(BalloonMenu), "BaseBalloonMenuStyle")]
    [TestCase(typeof(BalloonMenuOption), "BaseBalloonMenuOptionStyle")]
    public void ControlsResolveAssignedBalloonContracts(Type type, string baseKey)
    {
        new SharedUiResourceIntegrationTests().SharedResources_LoadTogetherAndProvideReusableControlContracts();
        var control = (BalloonButton)Activator.CreateInstance(type)!;
        var host = new System.Windows.Controls.Grid { Resources = Application.Current.Resources };
        host.Children.Add(control);
        UiTestPump.Layout(host, 100, 100);
        Assert.That(control.Style, Is.SameAs(control.FindResource(type)));
        Assert.That(control.Style.BasedOn, Is.SameAs(control.FindResource(baseKey)));
        UiControlContract.AssertTemplate(control, "BalloonControlTemplate");
    }
}
