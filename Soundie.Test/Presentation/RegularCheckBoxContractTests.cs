using System.Windows;
using System.Windows.Controls;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
namespace Soundie.Test.Presentation;
[Apartment(ApartmentState.STA), NonParallelizable]
public sealed class RegularCheckBoxContractTests
{
    [Test]
    public void CanonicalTemplatePreservesNativeThreeStateBehavior()
    {
        new SharedUiResourceIntegrationTests().SharedResources_LoadTogetherAndProvideReusableControlContracts();
        var control=new CheckBox { IsThreeState=true, Style=(Style)Application.Current.FindResource("RegularCheckBoxStyle") };
        UiTestPump.Layout(control,200,60);
        UiControlContract.AssertTemplate(control,"RegularCheckBoxTemplate");
        var peer=UIElementAutomationPeer.CreatePeerForElement(control);
        var provider=(IToggleProvider)peer.GetPattern(PatternInterface.Toggle);
        provider.Toggle(); Assert.That(control.IsChecked,Is.True);
        provider.Toggle(); Assert.That(control.IsChecked,Is.Null);
        provider.Toggle(); Assert.That(control.IsChecked,Is.False);
    }
}
