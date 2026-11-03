using CommunityToolkit.Mvvm.Messaging;
using Hitbench.Core.Entities;
using Hitbench.ViewModels.Shell.HomePage;
using Hitbench.Views.Pages;

namespace Hitbench.Test.Presentation;

[Apartment(ApartmentState.STA), NonParallelizable]
public sealed class MainWindowUpdateTests
{
    [Test]
    public void PreparingUpdateFreezesInputAndCancelledCloseRestoresIt()
    {
        new SongsResponsiveLayoutTests().SetUp();
        var saved = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var settings = new StubAppSettingsService { SaveWindowPlacement = (_, _) => saved.Task };
        var messenger = new WeakReferenceMessenger();
        var vm = new HomePageVM(new StubHomeFolderService(), settings, new StubInstallationDialogService(), new ImmediateUiScheduler(),
            new StubProjectFileService((_, _, _) => Task.FromResult("saved.json")), messenger);
        var window = TestShell.Create(new HomePage(vm), messenger, WorkstationTestPage.Create, vm, settings);
        var root = (System.Windows.FrameworkElement)window.FindName("ShellRoot");
        var preparing = window.PrepareForUpdateCloseAsync(default);
        try
        {
            Assert.That(root.IsEnabled, Is.False);
            saved.SetResult();
            UiTestPump.Run(preparing);
            Assert.That(preparing.Result, Is.True);
            System.ComponentModel.CancelEventHandler refuse = (_, args) => args.Cancel = true;
            window.Closing += refuse;
            var committed = false;
            var closing = window.RequestUpdateCloseAsync(() => committed = true, default);
            UiTestPump.Run(closing);
            Assert.That(closing.Result, Is.False);
            Assert.That(committed, Is.False);
            window.Closing -= refuse;
            window.CancelUpdateClose();
            Assert.That(root.IsEnabled, Is.True);
        }
        finally { saved.TrySetResult(); window.CancelUpdateClose(); window.Close(); vm.Deactivate(); }
    }

    [Test]
    public void PlacementFailurePreventsPreparedUpdateCloseWithoutShowingWindow()
    {
        new SongsResponsiveLayoutTests().SetUp();
        var settings = new StubAppSettingsService { SaveWindowPlacement = (_, _) => throw new IOException("disk full") };
        var messenger = new WeakReferenceMessenger();
        var vm = new HomePageVM(new StubHomeFolderService(), settings, new StubInstallationDialogService(), new ImmediateUiScheduler(),
            new StubProjectFileService((_, _, _) => Task.FromResult("saved.json")), messenger);
        var window = TestShell.Create(new HomePage(vm), messenger, WorkstationTestPage.Create, vm, settings);
        window.ApplySettings(new AppSettings());
        window.RecordWindowSize(1100, 700);
        var preparing = window.PrepareForUpdateCloseAsync(default);
        UiTestPump.Run(preparing);
        Assert.That(preparing.Result, Is.False);
        window.CancelUpdateClose();
        vm.Deactivate();
        window.Close();
        window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
    }
}
