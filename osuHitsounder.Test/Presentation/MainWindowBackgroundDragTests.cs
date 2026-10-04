using System.Windows;
using System.Windows.Controls;
using CommunityToolkit.Mvvm.Messaging;
using osuHitsounder.Services.Ui;
using osuHitsounder.ViewModels.Shell.HomePage;
using osuHitsounder.Views.Pages;

namespace osuHitsounder.Test.Presentation;

[Apartment(ApartmentState.STA), NonParallelizable]
public class MainWindowBackgroundDragTests
{
    [Test]
    public void RestoreGripUsesNegativeMonitorAndFractionalDpi()
    {
        Assert.That(OHsWindowFrame.GetRestoreBounds(new Point(-960, 540),
            new Rect(-1920, 0, 1920, 1080), new Size(1024, 640), 1.25, 1.5),
            Is.EqualTo(new Rect(-1600, 60, 1280, 960)));
    }

    [TestCase(false), TestCase(true)]
    public void RootDragRestoresBeforeEnteringDragLoopAndRechecksButton(bool release)
    {
        new SongsResponsiveLayoutTests().SetUp();
        var messenger = new WeakReferenceMessenger();
        var settings = new StubAppSettingsService();
        var vm = new HomePageVM(new StubHomeFolderService(), settings,
            new StubInstallationDialogService(), new StubProjectCreationDialogService(), new ImmediateUiScheduler(),
            new StubProjectFileService((_, _, _) => Task.FromResult("saved.json")), messenger);
        var calls = new List<string>();
        bool held = true;
        MainWindow? window = null;
        window = TestShell.Create(new HomePage(vm), messenger, WorkstationTestPage.Create, vm, settings,
            state => { calls.Add(state.ToString()); window!.WindowState = state; if (release) held = false; },
            () => calls.Add("Drag"), _ => calls.Add("Move"), () => held);
        window.WindowState = WindowState.Maximized;
        var root = (Border)window.FindName("ShellRoot");
        Assert.That(window.HandleBackgroundMouseDown(root.Child, 1, true, new Point(960, 540), new Rect(0, 0, 1920, 1080), 1, 1), Is.False);
        Assert.That(window.HandleBackgroundMouseDown(new TextBlock(), 1, true, new Point(), new Rect(0, 0, 1920, 1080), 1, 1), Is.False);
        vm.IsInteractionBlocked = true;
        Assert.That(window.HandleBackgroundMouseDown(root, 1, true, new Point(), new Rect(0, 0, 1920, 1080), 1, 1), Is.False);
        vm.IsInteractionBlocked = false;
        Assert.That(window.HandleBackgroundMouseDown(root, 1, false, new Point(), new Rect(0, 0, 1920, 1080), 1, 1), Is.False);
        Assert.That(window.HandleBackgroundMouseDown(root, 1, true, new Point(960, 540), new Rect(0, 0, 1920, 1080), 1, 1), Is.True);
        Assert.That(calls, Is.EqualTo(release ? new[] { "Normal", "Move" } : new[] { "Normal", "Move", "Drag" }));
        Assert.That(window.WindowState, Is.EqualTo(WindowState.Normal));
        calls.Clear();
        held = true;
        window.HandleBackgroundMouseDown(root, 2, true, new Point(), new Rect(0, 0, 1280, 720), 1, 1);
        Assert.That(calls, Is.EqualTo(new[] { "Maximized" }));
        window.Close();
    }
}
