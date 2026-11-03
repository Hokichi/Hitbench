using System.Windows;
using System.Windows.Automation;
using Hitbench.Views.Shell;
using Hitbench.Resources.CustomControls;

namespace Hitbench.Test.Presentation;

[Apartment(ApartmentState.STA), NonParallelizable]
public sealed class OHsShellHeaderTests
{
    [Test]
    public void DownloadAvailabilityAndRequestUseIndependentHeaderContract()
    {
        new SharedUiResourceIntegrationTests().SharedResources_LoadTogetherAndProvideReusableControlContracts();
        var header = new OHsShellHeader();
        var group = (FrameworkElement)header.FindName("UpdateActions");
        var button = (BalloonButton)header.FindName("DownloadUpdateButton");
        Assert.That(group.Visibility, Is.EqualTo(Visibility.Collapsed));
        header.IsUpdateAvailable = true;
        header.CanDownloadUpdate = true;
        Assert.That(group.Visibility, Is.EqualTo(Visibility.Visible));
        Assert.That(button.ShouldShowText, Is.False);
        Assert.That(AutomationProperties.GetName(button), Is.EqualTo("Download update"));
        Hitbench.Resources.Helpers.KeyboardNavigationPolicy.SetSuppressTabNavigation(header, true);
        Assert.That(button.IsTabStop, Is.False);
        Assert.That(button.ButtonIcon, Is.SameAs(header.FindResource("Download")));
        UiControlContract.AssertStyle<BalloonButton>(button, "OHsShellHeaderUpdateActionStyle");
        var requests = 0;
        header.DownloadUpdateRequested += (_, _) => requests++;
        button.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
        Assert.That(requests, Is.EqualTo(1));
        header.IsWorkstationVisible = true;
        header.IsWorkstationVisible = false;
        Assert.That(group.Visibility, Is.EqualTo(Visibility.Visible));
        header.IsUpdateAvailable = false;
        Assert.That(group.Visibility, Is.EqualTo(Visibility.Collapsed));
    }

    [TestCase("GoBackButton", 0)]
    [TestCase("MinimizeButton", 1)]
    [TestCase("MaximizeRestoreButton", 2)]
    [TestCase("CloseButton", 3)]
    public void ActionsRaiseOnlyTheirOwnRequest(string name, int expected)
    {
        new SharedUiResourceIntegrationTests().SharedResources_LoadTogetherAndProvideReusableControlContracts();
        var header = new OHsShellHeader { CanGoBack = true, IsWorkstationVisible = true };
        var requests = new List<int>();
        header.GoBackRequested += (_, _) => requests.Add(0);
        header.MinimizeRequested += (_, _) => requests.Add(1);
        header.MaximizeRestoreRequested += (_, _) => requests.Add(2);
        header.CloseRequested += (_, _) => requests.Add(3);
        var action = (BalloonButton)header.FindName(name);
        action.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
        Assert.That(requests, Is.EqualTo(new[] { expected }));
    }

    [Test]
    public void ReturnAvailabilityFollowsPublicContract()
    {
        new SharedUiResourceIntegrationTests().SharedResources_LoadTogetherAndProvideReusableControlContracts();
        var header = new OHsShellHeader { CanGoBack = false, IsWorkstationVisible = true };
        UiTestPump.Layout(header, 900, 100);
        var action = (BalloonButton)header.FindName("GoBackButton");
        Assert.That(action.IsEnabled, Is.False);
        header.CanGoBack = true;
        Assert.That(action.IsEnabled, Is.True);
    }

    [Test]
    public void HomeChromeRetainsVisibilityAndReturnContract()
    {
        new SharedUiResourceIntegrationTests().SharedResources_LoadTogetherAndProvideReusableControlContracts();
        var header = new OHsShellHeader { CanGoBack = true, IsWorkstationVisible = true };
        UiTestPump.Layout(header, 900, 100);
        var home = (BalloonButton)header.FindName("GoBackButton");
        var actions = (FrameworkElement)header.FindName("WorkstationActions");
        Assert.That(AutomationProperties.GetName(home), Is.EqualTo("Return to projects"));
        Assert.That(home.ButtonIcon, Is.SameAs(home.FindResource("AngleLeft")));
        Assert.That(home.FocusVisualStyle, Is.SameAs(home.FindResource("OHsSampleManagerKeyboardFocusStyle")));
        UiControlContract.AssertStyle<BalloonButton>(home, "OHsMainWindowGoBackStyle");
        header.CanGoBack = false;
        Assert.That(home.IsEnabled, Is.False);
        Assert.That(actions.Visibility, Is.EqualTo(Visibility.Visible));
        header.IsWorkstationVisible = false;
        Assert.That(actions.Visibility, Is.EqualTo(Visibility.Collapsed));
    }

    [Test]
    public void MaximizeStateUpdatesAccessibleAction()
    {
        new SharedUiResourceIntegrationTests().SharedResources_LoadTogetherAndProvideReusableControlContracts();
        var header = new OHsShellHeader { IsMaximized = true };
        var action = (BalloonButton)header.FindName("MaximizeRestoreButton");
        Assert.That(AutomationProperties.GetName(action), Is.EqualTo("Restore"));
        header.IsMaximized = false;
        Assert.That(action.ButtonText, Is.EqualTo("Maximize"));
        UiControlContract.AssertStyle<BalloonButton>(action, "OHsBalloonNeutralActionStyle");
    }
}

