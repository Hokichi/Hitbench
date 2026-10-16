using System.Windows;
using System.Windows.Automation;
using osuHitsounder.Views.Shell;
using osuHitsounder.Resources.CustomControls;

namespace osuHitsounder.Test.Presentation;

[Apartment(ApartmentState.STA), NonParallelizable]
public sealed class OHsShellHeaderTests
{
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

