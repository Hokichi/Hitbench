using osuHitsounder.Views.Pages;
using osuHitsounder.ViewModels.Shell;
namespace osuHitsounder.Test.Presentation;
[Apartment(ApartmentState.STA), NonParallelizable]
public sealed class OHsWorkstationAudioCardTests
{
    [Test]
    public void ComponentUsesExistingViewModelContract()
    {
        new SharedUiResourceIntegrationTests().SharedResources_LoadTogetherAndProvideReusableControlContracts();
        var vm = (WorkstationPageVM)WorkstationTestPage.Create().DataContext;
        var owner = new OHsWorkstationAudioCard { DataContext = vm };
        UiTestPump.Layout(owner, 900, 250);
        var action = (osuHitsounder.Resources.CustomControls.BalloonButton)owner.FindName("AddSampleTrackButton");
        Assert.That(action.Command, Is.SameAs(vm.AddSampleTrackCommand));
        Assert.That(owner.Timeline.DataContext, Is.SameAs(vm));
        UiTestPump.Run(vm.DeactivateAsync());
    }
}
