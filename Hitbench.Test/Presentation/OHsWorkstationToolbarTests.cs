using System.Windows.Controls;
using Hitbench.Resources.CustomControls;
using Hitbench.Views.Pages;

namespace Hitbench.Test.Presentation;

[Apartment(ApartmentState.STA), NonParallelizable]
public sealed class OHsWorkstationToolbarTests
{
    [Test]
    public void ToolbarMenusKeepOwnerBindingsAndAssignedMenuStyle()
    {
        new SharedUiResourceIntegrationTests().SharedResources_LoadTogetherAndProvideReusableControlContracts();
        var page = WorkstationTestPage.Create();
        var vm = (Hitbench.ViewModels.Shell.WorkstationPageVM)page.DataContext;
        try
        {
            UiTestPump.Layout(page, 1200, 800);
            foreach (var name in new[] { "ProjectActionsButton", "OpenFoldersButton", "AddMenuButton" })
            {
                var trigger = (BalloonComboBox)page.Toolbar.FindName(name);
                trigger.ContextMenu.PlacementTarget = trigger;
                UiTestPump.Layout(trigger.ContextMenu, 320, 300);
                Assert.That(trigger.ContextMenu, Is.TypeOf<ContextMenu>());
                Assert.That(trigger.ContextMenu.Style, Is.SameAs(trigger.FindResource("OHsWorkstationToolbarMenuStyle")));
                Assert.That(trigger.ContextMenu.Style.BasedOn, Is.SameAs(trigger.FindResource("OHsActionContextMenuStyle")));
                Assert.That(trigger.ContextMenu.DataContext, Is.SameAs(vm));
            }
        }
        finally { UiTestPump.Run(page.DeactivateAsync()); }
    }

    [TestCase(640)] [TestCase(1200)]
    public void ToolbarPreservesCommandsAndReadinessAcrossSampleDrawerAttachment(double width)
    {
        new SharedUiResourceIntegrationTests().SharedResources_LoadTogetherAndProvideReusableControlContracts();
        var page = WorkstationTestPage.Create();
        var vm = (Hitbench.ViewModels.Shell.WorkstationPageVM)page.DataContext;
        var play = (BalloonButton)page.Toolbar.FindName("PlayPauseButton");
        var host = new Grid();
        try
        {
            UiTestPump.Layout(page, width, 800);
            Assert.That(play.Command, Is.SameAs(vm.PlayPauseCommand));
            Assert.That(play.IsEnabled, Is.False);
            UiTestPump.Run(vm.LoadAsync("project.json", default));
            vm.AcknowledgePresentationReady(vm.LoadGeneration);
            UiTestPump.Layout(page, width, 800);
            Assert.That(play.IsEnabled, Is.True);
            page.AttachDrawerHost(host, Hitbench.Resources.DataModels.OHsWorkstationDrawerKind.Samples);
            page.SettleDrawerHostAnimation(host, true);
            UiTestPump.Layout(page, width, 800);
            Assert.That(play.Command, Is.SameAs(vm.PlayPauseCommand));
            Assert.That(play.IsEnabled, Is.True);
            Assert.That(play.IsTabStop, Is.False);
            page.DetachDrawerHost(host);
            Assert.That(play.Command, Is.SameAs(vm.PlayPauseCommand));
        }
        finally { page.DetachDrawerHost(host); UiTestPump.Run(page.DeactivateAsync()); }
    }

    [Test]
    public void AddMenuContainsOnlyTrackActions()
    {
        new SharedUiResourceIntegrationTests().SharedResources_LoadTogetherAndProvideReusableControlContracts();
        var toolbar = new OHsWorkstationToolbar();
        var add = (BalloonComboBox)toolbar.FindName("AddMenuButton");
        Assert.That(add.ContextMenu.Items.OfType<MenuItem>().Select(item => item.Header),
            Is.EqualTo(new[] { "Sample Track", "Instrumental Track" }));
    }
}
