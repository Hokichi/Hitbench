using Microsoft.Extensions.DependencyInjection;
using osuHitsounder.Resources.CustomControls;

using System.Windows.Media;
using System.Windows;

namespace osuHitsounder.Test.Presentation;

[Apartment(ApartmentState.STA)] [NonParallelizable]
public sealed class ManageSampleHeaderTests
{
    [Test] public void HeaderDoesNotOfferManagerAction()
    {
        new SharedUiResourceIntegrationTests().SharedResources_LoadTogetherAndProvideReusableControlContracts();
        using var services = App.CreateServices();
        var shell = services.GetRequiredService<MainWindow>();
        try
        {
            var actions = Descendants((DependencyObject)shell.Content).OfType<BalloonButton>();
            Assert.That(actions.Any(button => button.ButtonText is "Manage Sample" or "Manage Samples"), Is.False);
        }
        finally { shell.Close(); }
    }
    [Test] public void WorkstationOffersManagerWithExistingToolbarContract()
    {
        new SharedUiResourceIntegrationTests().SharedResources_LoadTogetherAndProvideReusableControlContracts();
        using var services = App.CreateServices();
        var page = services.GetRequiredService<Func<osuHitsounder.Views.Pages.WorkstationPage>>()();
        var host = new Window { Content = page };
        using var view = new InMemoryView(host, scheduleLayout: false);
        var button = (BalloonButton)page.FindName("ManageSamplesButton");
        var vm = (osuHitsounder.ViewModels.Shell.WorkstationPageVM)page.DataContext;
        Assert.That(button, Is.TypeOf<BalloonButton>());
        Assert.That(button.Command, Is.SameAs(vm.ManageSamplesCommand));
        Assert.That(button.Style, Is.SameAs(page.FindResource("OHsWorkstationToolbarButtonStyle")));
        Assert.That(button.Style, Is.SameAs(page.FindResource("OHsWorkstationToolbarButtonStyle")));
        Assert.That(button.IsTabStop, Is.True);
        Assert.That(((BalloonButton)page.FindName("AddSampleTrackButton")).Command, Is.SameAs(vm.AddSampleTrackCommand));
        Assert.That(((BalloonButton)page.FindName("AddInstrumentButton")).Command, Is.SameAs(vm.AddPianoTrackCommand));
        UiTestPump.Run(page.DeactivateAsync());
        host.Close();
    }
    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        yield return root; for (var i=0;i<VisualTreeHelper.GetChildrenCount(root);i++)
            foreach(var child in Descendants(VisualTreeHelper.GetChild(root,i))) yield return child;
    }
}

