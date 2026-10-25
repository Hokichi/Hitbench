using Microsoft.Extensions.DependencyInjection;
using Hitbench.Resources.CustomControls;

using System.Windows.Media;
using System.Windows;

namespace Hitbench.Test.Presentation;

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
        var page = services.GetRequiredService<Func<Hitbench.Views.Pages.WorkstationPage>>()();
        var host = new Window { Content = page };
        using var view = new InMemoryView(host, scheduleLayout: false);
        var button = (BalloonButton)((Hitbench.Views.Pages.OHsWorkstationAudioCard)page.FindName("AudioCard")).FindName("ManageSamplesButton");
        var vm = (Hitbench.ViewModels.Shell.WorkstationPageVM)page.DataContext;
        Assert.That(button, Is.TypeOf<BalloonButton>());
        Assert.That(button.Command, Is.SameAs(vm.ManageSamplesCommand));
        UiControlContract.AssertStyle<BalloonButton>(button, "OHsWorkstationSamplesActionStyle");
        Assert.That(button.Style.BasedOn, Is.SameAs(button.FindResource("OHsBalloonNavInfoActionStyle")));
        Assert.That(button.IsTabStop, Is.True);
        var trigger = (BalloonComboBox)page.AudioCard.FindName("AddMenuButton");
        // This unloaded DI page is disabled; supply the native placement boundary without opening its menu.
        trigger.ContextMenu.PlacementTarget = trigger;
        UiTestPump.Layout(trigger.ContextMenu, 320, 200);
        UiTestPump.Run(trigger.Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle).Task);
        Assert.That(((System.Windows.Controls.MenuItem)trigger.ContextMenu.Items[0]).Command, Is.SameAs(vm.AddSampleTrackCommand));
        Assert.That(((System.Windows.Controls.MenuItem)trigger.ContextMenu.Items[1]).Command, Is.SameAs(vm.AddPianoTrackCommand));
        UiTestPump.Run(page.DeactivateAsync());
        host.Close();
    }
    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        yield return root; for (var i=0;i<VisualTreeHelper.GetChildrenCount(root);i++)
            foreach(var child in Descendants(VisualTreeHelper.GetChild(root,i))) yield return child;
    }
}

