using System.Windows.Controls;
using System.Windows.Threading;
using System.Windows;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.DependencyInjection;
using osuHitsounder.ViewModels.Popups;
using osuHitsounder.Views.Popups;

namespace osuHitsounder.Test.Presentation;

[Apartment(ApartmentState.STA)] [NonParallelizable]
public sealed class ManageSamplesDrawerHostTests
{
    [Test]
    public void IdleBackdropClickClosesWithoutClickThrough()
    {
        new SharedUiResourceIntegrationTests().SharedResources_LoadTogetherAndProvideReusableControlContracts();
        using var services = App.CreateServices(); var shell = services.GetRequiredService<MainWindow>();
        var vm = CreateManager(); var drawer = new ManageSamplesDrawer(vm); var underlayEvents = 0;
        try
        {
            UiTestPump.Run(vm.InitializeAsync(default)); var showing = shell.PresentSampleManagerAsync(drawer, default, (_, _) => Task.CompletedTask);
            var backdrop = (Border?)shell.FindName("SampleManagerBackdrop"); Assert.That(backdrop, Is.Not.Null);
            var content = (FrameworkElement)shell.FindName("SampleManagerAppContent");
            content.MouseLeftButtonDown += (_, _) => underlayEvents++;
            var click = Click(backdrop!);
            Assert.That(click.Handled, Is.True); Assert.That(underlayEvents, Is.Zero);
            UiTestPump.Run(showing);
            Assert.That(((ContentControl)shell.FindName("SampleManagerHost")).Content, Is.Null);
        }
        finally { shell.DismissSampleManager(drawer); UiTestPump.Run(vm.DeactivateAsync()); shell.Close(); }
    }

    [TestCase("kick")][TestCase("CON")]
    public void BackdropClickCannotDismissEditingDrawer(string stem)
    {
        new SharedUiResourceIntegrationTests().SharedResources_LoadTogetherAndProvideReusableControlContracts();
        using var services = App.CreateServices(); var shell = services.GetRequiredService<MainWindow>();
        var vm = CreateManager(); var drawer = new ManageSamplesDrawer(vm);
        try
        {
            UiTestPump.Run(vm.InitializeAsync(default)); var showing = shell.PresentSampleManagerAsync(drawer, default, (_, _) => Task.CompletedTask);
            UiTestPump.Run(vm.EditCommand.ExecuteAsync(vm.Entries.Single())); vm.DraftStem = stem;
            var backdrop = (Border?)shell.FindName("SampleManagerBackdrop"); Assert.That(backdrop, Is.Not.Null);
            Assert.That(Click(backdrop!).Handled, Is.True);
            Assert.That(showing.IsCompleted, Is.False); Assert.That(vm.DraftStem, Is.EqualTo(stem));
            Assert.That(vm.ActiveEdit, Is.Not.Null); Assert.That(vm.Entries.Single().FileName, Is.EqualTo("hit.wav"));
        }
        finally { shell.DismissSampleManager(drawer); UiTestPump.Run(vm.DeactivateAsync()); shell.Close(); }
    }

    [Test]
    public void BackdropClickCannotDismissBusyDrawer()
    {
        new SharedUiResourceIntegrationTests().SharedResources_LoadTogetherAndProvideReusableControlContracts();
        using var services = App.CreateServices(); var shell = services.GetRequiredService<MainWindow>();
        var renamed = new TaskCompletionSource<string>();
        var files = new StubSampleFileService(["hit.wav"]) { Rename = (_, _, _, _) => renamed.Task };
        var vm = CreateManager(files); var drawer = new ManageSamplesDrawer(vm); Task? saving = null;
        var previousContext = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
        try
        {
            UiTestPump.Run(vm.InitializeAsync(default)); var showing = shell.PresentSampleManagerAsync(drawer, default, (_, _) => Task.CompletedTask);
            UiTestPump.Run(vm.EditCommand.ExecuteAsync(vm.Entries.Single())); vm.DraftStem = "kick";
            saving = vm.CommitRenameCommand.ExecuteAsync(null);
            var backdrop = (Border?)shell.FindName("SampleManagerBackdrop"); Assert.That(backdrop, Is.Not.Null);
            Assert.That(Click(backdrop!).Handled, Is.True); Assert.That(showing.IsCompleted, Is.False);
            Assert.That(vm.IsBusy, Is.True);
        }
        finally
        {
            renamed.TrySetResult("kick.wav"); if (saving is not null) UiTestPump.Run(saving);
            shell.DismissSampleManager(drawer); UiTestPump.Run(vm.DeactivateAsync()); shell.Close();
            SynchronizationContext.SetSynchronizationContext(previousContext);
        }
    }

    [Test]
    public void InsideClickDoesNotRequestDismissal()
    {
        new SharedUiResourceIntegrationTests().SharedResources_LoadTogetherAndProvideReusableControlContracts();
        using var services = App.CreateServices(); var shell = services.GetRequiredService<MainWindow>();
        var vm = CreateManager(); var drawer = new ManageSamplesDrawer(vm);
        try
        {
            UiTestPump.Run(vm.InitializeAsync(default)); var showing = shell.PresentSampleManagerAsync(drawer, default, (_, _) => Task.CompletedTask);
            Click(drawer); Assert.That(showing.IsCompleted, Is.False);
        }
        finally { shell.DismissSampleManager(drawer); UiTestPump.Run(vm.DeactivateAsync()); shell.Close(); }
    }

    [Test]
    public void BackgroundIsUnavailableWhileDrawerKeepsFocusScope()
    {
        new SharedUiResourceIntegrationTests().SharedResources_LoadTogetherAndProvideReusableControlContracts();
        using var services = App.CreateServices(); var shell = services.GetRequiredService<MainWindow>();
        var vm = CreateManager(); var drawer = new ManageSamplesDrawer(vm);
        try
        {
            UiTestPump.Run(vm.InitializeAsync(default)); var showing = shell.PresentSampleManagerAsync(drawer, default, (_, _) => Task.CompletedTask);
            UiTestPump.Layout((FrameworkElement)shell.Content, 1280, 720);
            var header = (Grid?)shell.FindName("ShellHeader"); Assert.That(header, Is.Not.Null);
            var content = (Border)shell.FindName("SampleManagerAppContent");
            Assert.That(header!.IsEnabled, Is.False); Assert.That(content.IsEnabled, Is.False); Assert.That(drawer.IsEnabled, Is.True);
            Assert.That(content.Style, Is.SameAs(shell.FindResource("OHsMainWindowSampleManagerContentStyle")));
            Assert.That(content.Style.BasedOn, Is.SameAs(content.FindResource("BaseBorderStyle")));
            var backdrop = (Border)shell.FindName("SampleManagerBackdrop");
            Assert.That(backdrop.Style, Is.SameAs(shell.FindResource("OHsMainWindowSampleManagerBackdropStyle")));
            Assert.That(KeyboardNavigation.GetTabNavigation(drawer), Is.EqualTo(KeyboardNavigationMode.Cycle));
            shell.DismissSampleManager(drawer); UiTestPump.Run(showing);
        }
        finally { shell.DismissSampleManager(drawer); UiTestPump.Run(vm.DeactivateAsync()); shell.Close(); }
    }

    [Test]
    public void DrawerBackdropIsIndependentOfNestedModalOverlay()
    {
        new SharedUiResourceIntegrationTests().SharedResources_LoadTogetherAndProvideReusableControlContracts();
        using var services = App.CreateServices(); var shell = services.GetRequiredService<MainWindow>();
        var vm = CreateManager(); var drawer = new ManageSamplesDrawer(vm);
        try
        {
            UiTestPump.Run(vm.InitializeAsync(default)); var showing = shell.PresentSampleManagerAsync(drawer, default, (_, _) => Task.CompletedTask);
            var layer = (Grid?)shell.FindName("SampleManagerLayer"); Assert.That(layer, Is.Not.Null);
            shell.ShowPopupOverlay(); Assert.That(shell.IsPopupOverlayVisible, Is.True);
            shell.HidePopupOverlay(); Assert.That(shell.IsPopupOverlayVisible, Is.False); Assert.That(showing.IsCompleted, Is.False);
            Assert.That(((ContentControl)shell.FindName("SampleManagerHost")).Content, Is.SameAs(drawer));
        }
        finally { shell.DismissSampleManager(drawer); UiTestPump.Run(vm.DeactivateAsync()); shell.Close(); }
    }

    private static MouseButtonEventArgs Click(UIElement target)
    {
        var args = new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left)
        { RoutedEvent = UIElement.MouseLeftButtonDownEvent };
        target.RaiseEvent(args); return args;
    }

    [Test]
    public void PreparingShellCloseDismissesOpenManager()
    {
        new SharedUiResourceIntegrationTests().SharedResources_LoadTogetherAndProvideReusableControlContracts();
        using var services = App.CreateServices(); var shell = services.GetRequiredService<MainWindow>();
        var vm = CreateManager(); var drawer = new ManageSamplesDrawer(vm);
        try
        {
            UiTestPump.Run(vm.InitializeAsync(default)); var showing = shell.PresentSampleManagerAsync(drawer, default, (_, _) => Task.CompletedTask);
            UiTestPump.Run(shell.PrepareProjectForCloseAsync());
            Assert.That(((ContentControl)shell.FindName("SampleManagerHost")).Content, Is.Null);
            UiTestPump.Run(showing);
        }
        finally { shell.DismissSampleManager(drawer); UiTestPump.Run(vm.DeactivateAsync()); shell.Close(); }
    }

    [Test]
    public void CancellationRemovesDrawerWithoutModalStateAndRejectsDuplicateHost()
    {
        new SharedUiResourceIntegrationTests().SharedResources_LoadTogetherAndProvideReusableControlContracts();
        using var services = App.CreateServices(); var shell = services.GetRequiredService<MainWindow>();
        var vm = CreateManager(); var drawer = new ManageSamplesDrawer(vm);
        using var cancellation = new CancellationTokenSource();
        try
        {
            UiTestPump.Run(vm.InitializeAsync(default));
            var showing = shell.PresentSampleManagerAsync(drawer, cancellation.Token, (_, _) => Task.CompletedTask);
            var host = (ContentControl)shell.FindName("SampleManagerHost");
            Assert.That(host.Content, Is.SameAs(drawer)); Assert.That(showing.IsCompleted, Is.False);
            Assert.That(host.Style, Is.SameAs(shell.FindResource("OHsMainWindowSampleManagerHostStyle")));
            Assert.That(shell.IsPopupOverlayVisible, Is.False);
            Assert.ThrowsAsync<InvalidOperationException>(() => shell.PresentSampleManagerAsync(new ManageSamplesDrawer(vm), default));
            UiTestPump.Run(vm.EditCommand.ExecuteAsync(vm.Entries.Single())); vm.DraftStem = "kick";
            cancellation.Cancel(); UiTestPump.Run(showing);
            Assert.That(host.Content, Is.Null); Assert.That(vm.Entries.Single().FileName, Is.EqualTo("hit.wav"));
        }
        finally { cancellation.Cancel(); UiTestPump.Run(vm.DeactivateAsync()); shell.Close(); }
    }

    [Test]
    public void IdleCloseCompletesPresentationAndStaleDismissalCannotCloseNewDrawer()
    {
        new SharedUiResourceIntegrationTests().SharedResources_LoadTogetherAndProvideReusableControlContracts();
        using var services = App.CreateServices(); var shell = services.GetRequiredService<MainWindow>();
        var firstVM = CreateManager(); var secondVM = CreateManager();
        var first = new ManageSamplesDrawer(firstVM); var second = new ManageSamplesDrawer(secondVM);
        try
        {
            UiTestPump.Run(firstVM.InitializeAsync(default)); UiTestPump.Run(secondVM.InitializeAsync(default));
            var showing = shell.PresentSampleManagerAsync(first, default, (_, _) => Task.CompletedTask);
            first.HandleEscape(); UiTestPump.Run(showing);
            var next = shell.PresentSampleManagerAsync(second, default, (_, _) => Task.CompletedTask);
            shell.DismissSampleManager(first); Assert.That(next.IsCompleted, Is.False);
            Assert.That(((ContentControl)shell.FindName("SampleManagerHost")).Content, Is.SameAs(second));
            shell.DismissSampleManager(second); UiTestPump.Run(next);
        }
        finally { shell.DismissSampleManager(second); UiTestPump.Run(firstVM.DeactivateAsync()); UiTestPump.Run(secondVM.DeactivateAsync()); shell.Close(); }
    }

    private static ManageSamplesPopupVM CreateManager(StubSampleFileService? files = null)
    {
        files ??= new StubSampleFileService(["hit.wav"]);
        return new(new("project.json", null, Guid.NewGuid()), files, new(files), new StubSampleFileDialogService([]),
            new StubSampleRemovalDialogService(), new WeakReferenceMessenger(), new osuHitsounder.Services.Ui.UiScheduler(Dispatcher.CurrentDispatcher));
    }
}
