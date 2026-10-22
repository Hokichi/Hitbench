using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using CommunityToolkit.Mvvm.Messaging;
using Soundie.Services.Ui;
using Soundie.ViewModels.Shell.HomePage;
using Soundie.Views.Pages;

namespace Soundie.Test.Presentation;

[Apartment(ApartmentState.STA), NonParallelizable]
public class MainWindowBackgroundDragTests
{
    private MainWindow window = null!;
    private HomePageVM vm = null!;
    private Border root = null!;
    private readonly List<string> calls = [];
    private bool held;
    private bool releaseDuringRestore;
    private static readonly Point Origin = new(-960, 540);
    private static readonly Rect Bounds = new(-1920, 0, 1920, 1080);

    [SetUp]
    public void SetUp()
    {
        new SongsResponsiveLayoutTests().SetUp();
        var messenger = new WeakReferenceMessenger();
        var settings = new StubAppSettingsService();
        vm = new HomePageVM(new StubHomeFolderService(), settings,
            new StubInstallationDialogService(), new StubProjectCreationDialogService(), new ImmediateUiScheduler(),
            new StubProjectFileService((_, _, _) => Task.FromResult("saved.json")), messenger);
        calls.Clear();
        held = true;
        releaseDuringRestore = false;
        window = TestShell.Create(new HomePage(vm), messenger, WorkstationTestPage.Create, vm, settings,
            state => { calls.Add(state.ToString()); window.WindowState = state; if (releaseDuringRestore) held = false; },
            () => calls.Add("Drag"), _ => calls.Add("Move"), () => held);
        window.WindowState = WindowState.Maximized;
        root = (Border)window.FindName("ShellRoot");
    }

    [TearDown]
    public void TearDown() => window.Close();

    private bool Press(int count = 1) => window.HandleBackgroundMouseDown(root, count, true, Origin, Bounds, 1, 1);

    private bool Move(double x = 100, double y = 100, bool pressed = true, double dpiX = 1, double dpiY = 1) =>
        window.HandleBackgroundMouseMove(pressed, new Point(Origin.X + x, Origin.Y + y), Bounds, dpiX, dpiY);

    [Test]
    public void RestoreGripUsesNegativeMonitorAndFractionalDpi()
    {
        Assert.That(OHsWindowFrame.GetRestoreBounds(new Point(-960, 540),
            new Rect(-1920, 0, 1920, 1080), new Size(1024, 640), 1.25, 1.5),
            Is.EqualTo(new Rect(-1600, 60, 1280, 960)));
    }

    [TestCase(false), TestCase(true)]
    public void RootDragRestoresAfterMovementAndRechecksButton(bool release)
    {
        releaseDuringRestore = release;
        Assert.That(window.HandleBackgroundMouseDown(root.Child, 1, true, Origin, Bounds, 1, 1), Is.False);
        Assert.That(window.HandleBackgroundMouseDown(new TextBlock(), 1, true, Origin, Bounds, 1, 1), Is.False);
        vm.IsInteractionBlocked = true;
        Assert.That(window.HandleBackgroundMouseDown(root, 1, true, new Point(), new Rect(0, 0, 1920, 1080), 1, 1), Is.False);
        vm.IsInteractionBlocked = false;
        Assert.That(window.HandleBackgroundMouseDown(root, 1, false, new Point(), new Rect(0, 0, 1920, 1080), 1, 1), Is.False);
        Assert.That(Press(), Is.True);
        Assert.That(calls, Is.Empty);
        Assert.That(Move(), Is.True);
        Assert.That(calls, Is.EqualTo(release ? new[] { "Normal", "Move" } : new[] { "Normal", "Move", "Drag" }));
        Assert.That(window.WindowState, Is.EqualTo(WindowState.Normal));
        calls.Clear();
        Assert.That(Move(), Is.False);
        Assert.That(calls, Is.Empty);
    }

    [Test]
    public void MaximizedSingleClickDoesNotRestore()
    {
        Press();
        root.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left)
        { RoutedEvent = Mouse.MouseUpEvent });
        Assert.That(Move(), Is.False);
        Assert.That(window.WindowState, Is.EqualTo(WindowState.Maximized));
        Assert.That(calls, Is.Empty);
    }

    [TestCase(true, 1, 1), TestCase(false, 1, 1)]
    [TestCase(true, 1.25, 1.5), TestCase(false, 1.25, 1.5)]
    public void MaximizedDragWaitsForEitherAxisThreshold(bool horizontal, double dpiX, double dpiY)
    {
        window.HandleBackgroundMouseDown(root, 1, true, Origin, Bounds, dpiX, dpiY);
        double x = SystemParameters.MinimumHorizontalDragDistance * dpiX;
        double y = SystemParameters.MinimumVerticalDragDistance * dpiY;
        Assert.That(Move(x / 2, y / 2, dpiX: dpiX, dpiY: dpiY), Is.False);
        Assert.That(Move(x, y, dpiX: dpiX, dpiY: dpiY), Is.False);
        Assert.That(calls, Is.Empty);
        Assert.That(window.WindowState, Is.EqualTo(WindowState.Maximized));
        Assert.That(Move(horizontal ? -x - 1 : 0, horizontal ? 0 : -y - 1,
            dpiX: dpiX, dpiY: dpiY), Is.True);
        Assert.That(calls, Is.EqualTo(new[] { "Normal", "Move", "Drag" }));
    }

    [TestCase(WindowState.Maximized, WindowState.Normal)]
    [TestCase(WindowState.Normal, WindowState.Maximized)]
    public void DoubleClickTogglesOnce(WindowState initial, WindowState expected)
    {
        window.WindowState = initial;
        if (initial == WindowState.Maximized) Press();
        Assert.That(Press(2), Is.True);
        Assert.That(calls, Is.EqualTo(new[] { expected.ToString() }));
        Assert.That(window.WindowState, Is.EqualTo(expected));
        Assert.That(Move(), Is.False);
    }

    [Test]
    public void NormalSinglePressStartsExistingDrag()
    {
        window.WindowState = WindowState.Normal;
        Assert.That(Press(), Is.True);
        Assert.That(calls, Is.EqualTo(new[] { "Normal", "Drag" }));
    }

    [TestCase("release"), TestCase("capture"), TestCase("deactivate"), TestCase("close")]
    [TestCase("state"), TestCase("blocked"), TestCase("processing"), TestCase("modal"), TestCase("popup")]
    public void CancelledOrInvalidatedGestureDoesNotResume(string reason)
    {
        Press();
        calls.Clear();
        switch (reason)
        {
            case "release": Move(pressed: false); break;
            case "capture": root.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, 0)
                { RoutedEvent = Mouse.LostMouseCaptureEvent }); break;
            case "deactivate": window.OnWindowDeactivated(window, EventArgs.Empty); break;
            case "close": window.Close(); break;
            case "state": window.WindowState = WindowState.Normal; window.WindowState = WindowState.Maximized; break;
            case "blocked": vm.IsInteractionBlocked = true; vm.IsInteractionBlocked = false; break;
            case "processing": vm.IsProcessing = true; vm.IsProcessing = false; break;
            case "modal": ComponentDispatcher.PushModal(); ComponentDispatcher.PopModal(); break;
            case "popup": window.ShowPopupOverlay(); window.HidePopupOverlay(); break;
        }
        Assert.That(Move(), Is.False);
        Assert.That(calls, Is.Empty);
    }
}
