using Hitbench.Views.Pages;
using Hitbench.ViewModels.Shell;
namespace Hitbench.Test.Presentation;
[Apartment(ApartmentState.STA), NonParallelizable]
public sealed class OHsWorkstationFooterTests
{
    [Test]
    public void HeaderReflowPreservesTimelineSettingsBindings()
    {
        new SharedUiResourceIntegrationTests().SharedResources_LoadTogetherAndProvideReusableControlContracts();
        var page = WorkstationTestPage.Create();
        var vm = (WorkstationPageVM)page.DataContext;
        var audio = (OHsWorkstationAudioCard)page.FindName("AudioCard");
        var footer = audio.Footer;
        var slider = (Hitbench.Resources.CustomControls.ValueSlider)footer.FindName("SubdivisionSelector");
        try
        {
            foreach (var size in new[] { new System.Windows.Size(1280, 720), new System.Windows.Size(640, 360), new System.Windows.Size(1280, 720) })
            {
                UiTestPump.Layout(page, size.Width, size.Height);
                Assert.That(audio.Footer, Is.SameAs(footer));
                Assert.That(footer.DataContext, Is.SameAs(vm));
                vm.SelectedSubdivisionIndex = 4;
                Assert.That(slider.Value, Is.EqualTo(4));
                slider.Value = 2;
                Assert.That(vm.SelectedSubdivisionIndex, Is.EqualTo(2));
                Assert.That(audio.ContainsInputOrigin(slider), Is.False);
                var zoom = (System.Windows.Controls.StackPanel)footer.FindName("ZoomControls");
                Assert.That(zoom.Children.OfType<Hitbench.Resources.CustomControls.BalloonButton>().Select(button => button.Command),
                    Is.EquivalentTo(new System.Windows.Input.ICommand[] { vm.ZoomOutCommand, vm.ZoomInCommand }));
            }
        }
        finally { UiTestPump.Run(page.DeactivateAsync()); }
    }

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
