using System.Windows;
using System.Windows.Controls;
using CommunityToolkit.Mvvm.Input;
using osuHitsounder.Resources.CustomControls;

namespace osuHitsounder.Test.Presentation;

[Apartment(ApartmentState.STA), NonParallelizable]
public sealed class BalloonControlBehaviorTests
{
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
            Source = new Uri($"/osuHitsounder;component/Resources/Themes/{theme}.xaml", UriKind.Relative)
        };
        var button = new BalloonButton { DataContext = new BalloonToggleState(), Style = (Style)resources[key], ButtonText = "Action", ShouldShowText = true,
            ButtonIcon = resources["Check"] };
        UiControlContract.AssertStyle<BalloonButton>(button, key);
        UiControlContract.AssertTemplate(button, "BalloonControlTemplate");
        Assert.That(button.ButtonText, Is.EqualTo("Action"));
    }
}
