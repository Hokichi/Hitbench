using System.Windows;
using System.Windows.Automation;
using osuHitsounder.Views.Shell;
using osuHitsounder.Resources.CustomControls;

namespace osuHitsounder.Test.Presentation;

[Apartment(ApartmentState.STA), NonParallelizable]
public sealed class OHsShellHeaderTests
{
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

