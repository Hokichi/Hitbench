using System.Windows;
using Hitbench.Resources.CustomControls;
using Hitbench.Resources.Helpers;
using Hitbench.Services;
using Hitbench.ViewModels.Shell;
using Hitbench.Views.Pages;

namespace Hitbench.Test.Presentation;

[Apartment(ApartmentState.STA), NonParallelizable]
public sealed class OHsMapsetToolbarTests
{
    [Test]
    public void ImportOptionBindsCommandAndSharedMenuContractWithoutOpeningMenu()
    {
        new SharedUiResourceIntegrationTests().SharedResources_LoadTogetherAndProvideReusableControlContracts();
        var vm = new WorkstationPageVM(new StubWorkstationSourceService(), new MarkerPlaybackCoordinator(new StubWorkstationAudioService(), Task.Delay), new ImmediateUiScheduler());
        var toolbar = new OHsWorkstationToolbar { DataContext = vm }; KeyboardNavigationPolicy.SetSuppressTabNavigation(toolbar, true);
        var host = new Window { Content = toolbar, Width = 900, Height = 400 };
        try
        {
            using var view = new InMemoryView(host, scheduleLayout: false);
            var menu = (BalloonMenu)toolbar.FindName("ProjectActionsButton");
            WorkstationActionMenus.Prepare(menu);
            var action = menu.Options.Single(option => option.ButtonText == "Import mapset samples");
            UiControlContract.AssertStyle<BalloonMenuOption>(action, "BaseBalloonMenuOptionStyle");
            Assert.That(action.Command, Is.SameAs(vm.ImportFromMapsetCommand)); Assert.That(action.IsEnabled, Is.False);
            Assert.That(action.ButtonIcon, Is.SameAs(action.FindResource("Download"))); Assert.That(action.IsTabStop, Is.False);
        }
        finally { UiTestPump.Run(vm.DeactivateAsync()); host.Close(); }
    }
}