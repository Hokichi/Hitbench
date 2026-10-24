using Hitbench.Views.Pages;
using Hitbench.ViewModels.Shell;
namespace Hitbench.Test.Presentation;
[Apartment(ApartmentState.STA), NonParallelizable]
public sealed class OHsWorkstationFooterTests
{
    [Test]
    public void ComponentUsesExistingViewModelContract()
    {
        new SharedUiResourceIntegrationTests().SharedResources_LoadTogetherAndProvideReusableControlContracts();
        var vm = (WorkstationPageVM)WorkstationTestPage.Create().DataContext;
        var owner = new OHsWorkstationFooter { DataContext = vm };
        UiTestPump.Layout(owner, 900, 250);
        vm.SelectedSubdivisionIndex = 4;
        var input = (Hitbench.Resources.CustomControls.ValueSlider)owner.FindName("SubdivisionSelector");
        Assert.That(input.Value, Is.EqualTo(vm.SelectedSubdivisionIndex));
        UiTestPump.Run(vm.DeactivateAsync());
    }
}
