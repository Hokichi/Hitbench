using System.Windows.Controls;
using System.Windows;
using System.Windows.Automation;
using Hitbench.Resources.CustomControls;
using Hitbench.Views.Pages;

namespace Hitbench.Test.Presentation;

[Apartment(ApartmentState.STA), NonParallelizable]
public sealed class OHsWorkstationToolbarTests
{
    [Test]
    public void OpenFolderOptionsDispatchExistingCommandsWithoutLaunchingProcesses()
    {
        new SharedUiResourceIntegrationTests().SharedResources_LoadTogetherAndProvideReusableControlContracts();
        var launcher = new StubDirectoryLauncher();
        var project = new Hitbench.Core.Entities.ProjectData { BeatmapPath = Path.GetFullPath("beatmap") };
        var source = new StubWorkstationSourceService { Load = (_, _) => Task.FromResult(
            new Hitbench.Core.Entities.WorkstationSource("map.osu", "audio.wav", project)) };
        var vm = new Hitbench.ViewModels.Shell.WorkstationPageVM(source,
            new Hitbench.Services.MarkerPlaybackCoordinator(new StubWorkstationAudioService(), Task.Delay),
            new ImmediateUiScheduler(), directoryLauncher: launcher);
        var toolbar = new OHsWorkstationToolbar { DataContext = vm };
        try
        {
            UiTestPump.Run(vm.LoadAsync(Path.GetFullPath("project/project.json"), default));
            vm.AcknowledgePresentationReady(vm.LoadGeneration);
            UiTestPump.Layout(toolbar, 900, 100);
            var menu = (BalloonMenu)toolbar.FindName("OpenFoldersButton");
            WorkstationActionMenus.Prepare(menu);
            WorkstationActionMenus.Invoke(menu.Options[0]);
            UiTestPump.Run(vm.OpenBeatmapFolderCommand.ExecutionTask ?? Task.CompletedTask);
            WorkstationActionMenus.Invoke(menu.Options[1]);
            UiTestPump.Run(vm.OpenProjectFolderCommand.ExecutionTask ?? Task.CompletedTask);
            Assert.That(launcher.Directories, Is.EqualTo(new[] { project.BeatmapPath, Path.GetFullPath("project") }));
        }
        finally { toolbar.CloseMenus(); UiTestPump.Run(vm.DeactivateAsync()); }
    }

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
                var trigger = (BalloonMenu)page.Toolbar.FindName(name);
                WorkstationActionMenus.Prepare(trigger);
                UiControlContract.AssertTemplate(trigger, "BalloonControlTemplate");
                Assert.That(trigger.Style.BasedOn, Is.SameAs(trigger.FindResource(name == "OpenFoldersButton" ?
                    "OHsWorkstationProjectActionsStyle" : "BaseBalloonMenuStyle")));
                foreach (var option in trigger.Options)
                {
                    Assert.That(option, Is.TypeOf<BalloonMenuOption>());
                    Assert.That(option.DataContext, Is.SameAs(vm));
                    Assert.That(AutomationProperties.GetName(option), Is.Not.Empty);
                    Assert.That(option.ShouldShowText, Is.False);
                }
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
        var add = (BalloonMenu)toolbar.FindName("AddMenuButton");
        Assert.That(add.Options.Select(item => item.ButtonText),
            Is.EqualTo(new[] { "Sample Track", "Instrumental Track" }));
    }

    [Test]
    public void OnlyOneToolbarMenuOwnsPresentation()
    {
        new SharedUiResourceIntegrationTests().SharedResources_LoadTogetherAndProvideReusableControlContracts();
        var toolbar = new OHsWorkstationToolbar();
        UiTestPump.Layout(toolbar, 900, 100);
        BalloonMenu? previous = null;
        try
        {
            foreach (var name in new[] { "ProjectActionsButton", "OpenFoldersButton", "AddMenuButton" })
            {
                var menu = (BalloonMenu)toolbar.FindName(name);
                WorkstationActionMenus.Open(menu);
                Assert.That(menu.IsExpanded, Is.True);
                if (previous is not null) Assert.That(previous.IsExpanded, Is.False);
                previous = menu;
            }
            toolbar.CloseMenus();
            Assert.That(previous!.IsExpanded, Is.False);
        }
        finally { toolbar.CloseMenus(); }
    }

    [TestCase("disable")] [TestCase("unload")] [TestCase("owner")] [TestCase("deactivate")]
    public void ToolbarTeardownClosesBeforeNavigation(string action)
    {
        new SharedUiResourceIntegrationTests().SharedResources_LoadTogetherAndProvideReusableControlContracts();
        var page = WorkstationTestPage.Create();
        try
        {
            UiTestPump.Layout(page, 900, 600);
            var menu = (BalloonMenu)page.Toolbar.FindName("ProjectActionsButton");
            WorkstationActionMenus.Open(menu);
            if (action == "disable") page.Toolbar.IsEnabled = false;
            else if (action == "unload") page.Toolbar.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent));
            else if (action == "owner") page.Toolbar.DataContext = new object();
            else { var completion = page.DeactivateAsync(); Assert.That(menu.IsExpanded, Is.False); UiTestPump.Run(completion); }
            Assert.That(menu.IsExpanded, Is.False);
        }
        finally { UiTestPump.Run(page.DeactivateAsync()); }
    }

    [TestCase(640, FlowDirection.LeftToRight)] [TestCase(1200, FlowDirection.LeftToRight)]
    [TestCase(640, FlowDirection.RightToLeft)] [TestCase(1200, FlowDirection.RightToLeft)]
    public void RevealKeepsEligibleOptionsInvokable(int width, FlowDirection direction)
    {
        new SharedUiResourceIntegrationTests().SharedResources_LoadTogetherAndProvideReusableControlContracts();
        var toolbar = new OHsWorkstationToolbar { FlowDirection = direction };
        UiTestPump.Layout(toolbar, width, 100);
        try
        {
            foreach (var name in new[] { "ProjectActionsButton", "OpenFoldersButton", "AddMenuButton" })
            {
                var menu = (BalloonMenu)toolbar.FindName(name);
                WorkstationActionMenus.Open(menu);
                UiTestPump.Layout(toolbar, width, 100);
                Assert.That(menu.Options.Where(option => option.IsEnabled).All(menu.CanInvokeOption), Is.True);
                menu.CloseMenu();
            }
        }
        finally { toolbar.CloseMenus(); }
    }
}
