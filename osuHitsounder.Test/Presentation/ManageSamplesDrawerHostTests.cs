using System.Windows.Controls;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.DependencyInjection;
using osuHitsounder.ViewModels.Popups;
using osuHitsounder.Views.Popups;

namespace osuHitsounder.Test.Presentation;

[Apartment(ApartmentState.STA)] [NonParallelizable]
public sealed class ManageSamplesDrawerHostTests
{
    [Test]
    public void PreparingShellCloseDismissesOpenManager()
    {
        new SharedUiResourceIntegrationTests().SharedResources_LoadTogetherAndProvideReusableControlContracts();
        using var services = App.CreateServices(); var shell = services.GetRequiredService<MainWindow>();
        var vm = CreateManager(); var drawer = new ManageSamplesDrawer(vm);
        try
        {
            UiTestPump.Run(vm.InitializeAsync(default)); var showing = shell.PresentSampleManagerAsync(drawer, default);
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
            var showing = shell.PresentSampleManagerAsync(drawer, cancellation.Token);
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
            var showing = shell.PresentSampleManagerAsync(first, default);
            first.HandleEscape(); UiTestPump.Run(showing);
            var next = shell.PresentSampleManagerAsync(second, default);
            shell.DismissSampleManager(first); Assert.That(next.IsCompleted, Is.False);
            Assert.That(((ContentControl)shell.FindName("SampleManagerHost")).Content, Is.SameAs(second));
            shell.DismissSampleManager(second); UiTestPump.Run(next);
        }
        finally { shell.DismissSampleManager(second); UiTestPump.Run(firstVM.DeactivateAsync()); UiTestPump.Run(secondVM.DeactivateAsync()); shell.Close(); }
    }

    private static ManageSamplesPopupVM CreateManager()
    {
        var files = new StubSampleFileService(["hit.wav"]);
        return new(new("project.json", null, Guid.NewGuid()), files, new(files), new StubSampleFileDialogService([]),
            new StubSampleRemovalDialogService(), new WeakReferenceMessenger(), new osuHitsounder.Services.Ui.UiScheduler(Dispatcher.CurrentDispatcher));
    }
}
