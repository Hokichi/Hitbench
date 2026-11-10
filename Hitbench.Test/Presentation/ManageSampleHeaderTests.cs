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
        using var services = App.CreateServices(null, null, new Hitbench.Services.Ui.UiScheduler(System.Windows.Threading.Dispatcher.CurrentDispatcher));
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
        using var services = App.CreateServices(null, null, new Hitbench.Services.Ui.UiScheduler(System.Windows.Threading.Dispatcher.CurrentDispatcher));
        var page = services.GetRequiredService<Func<Hitbench.Views.Pages.WorkstationPage>>()();
        var host = new Window { Content = page };
        using var view = new InMemoryView(host, scheduleLayout: false);
        var button = (BalloonButton)page.Toolbar.FindName("ManageSamplesButton");
        var vm = (Hitbench.ViewModels.Shell.WorkstationPageVM)page.DataContext;
        Assert.That(button, Is.TypeOf<BalloonButton>());
        Assert.That(button.Command, Is.SameAs(vm.ManageSamplesCommand));
        UiControlContract.AssertStyle<BalloonButton>(button, "OHsWorkstationSamplesActionStyle");
        Assert.That(button.Style.BasedOn, Is.SameAs(button.FindResource("OHsBalloonNavInfoActionStyle")));
        Assert.That(button.IsTabStop, Is.False);
        Assert.That(System.Windows.Automation.AutomationProperties.GetItemStatus(button), Is.EqualTo("Closed"));
        var trigger = (BalloonMenu)page.Toolbar.FindName("AddMenuButton");
        // This unloaded DI page is disabled; supply the native placement boundary without opening its menu.
        WorkstationActionMenus.Prepare(trigger);
        UiTestPump.Run(trigger.Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle).Task);
        Assert.That(trigger.Options[0].Command, Is.SameAs(vm.AddSampleTrackCommand));
        Assert.That(trigger.Options[1].Command, Is.SameAs(vm.AddPianoTrackCommand));
        UiTestPump.Run(page.DeactivateAsync());
        host.Close();
    }
    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        yield return root; for (var i=0;i<VisualTreeHelper.GetChildrenCount(root);i++)
            foreach(var child in Descendants(VisualTreeHelper.GetChild(root,i))) yield return child;
    }
}

