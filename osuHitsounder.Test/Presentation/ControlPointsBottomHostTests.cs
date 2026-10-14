using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.DependencyInjection;
using osuHitsounder.Resources.DataModels;
using osuHitsounder.Services;
using osuHitsounder.ViewModels.Popups;
using osuHitsounder.ViewModels.Shell;
using osuHitsounder.Views.Pages;
using osuHitsounder.Views.Popups;

namespace osuHitsounder.Test.Presentation;

[Apartment(ApartmentState.STA), NonParallelizable]
public class ControlPointsBottomHostTests
{
    private ServiceProvider services = null!;
    private MainWindow shell = null!;
    private WorkstationPage page = null!;
    private WorkstationPageVM owner = null!;
    private ControlPointsDrawerVM editor = null!;
    private OHsControlPointsDrawer drawer = null!;
    private CancellationTokenSource cancellation = null!;
    private SynchronizationContext? previousContext;
    private Task showing = Task.CompletedTask;

    [SetUp]
    public void SetUp()
    {
        new SharedUiResourceIntegrationTests().SharedResources_LoadTogetherAndProvideReusableControlContracts();
        services = App.CreateServices(); shell = services.GetRequiredService<MainWindow>();
        previousContext = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(shell.Dispatcher));
        owner = new(new StubWorkstationSourceService(), new MarkerPlaybackCoordinator(new StubWorkstationAudioService(), Task.Delay), new ImmediateUiScheduler());
        page = new(owner); UiTestPump.Run(owner.LoadAsync("project.json", default)); owner.AcknowledgePresentationReady(owner.LoadGeneration);
        ((ContentControl)shell.FindName("PageHost")).Content = page;
        editor = new(new(new("project.json", Guid.NewGuid(), Guid.NewGuid()), [], 1, true, null, false, null), new WeakReferenceMessenger(), new ImmediateUiScheduler());
        drawer = new(editor); cancellation = new(); showing = Task.CompletedTask;
    }
    private static void Drain() => UiTestPump.Run(Dispatcher.CurrentDispatcher.InvokeAsync(() => { }, DispatcherPriority.Background).Task);
    private void Show(bool bottom, Func<bool, CancellationToken, Task>? transition = null)
    {
        showing = shell.PresentDrawerAsync(drawer, cancellation.Token, transition ?? ((_, _) => Task.CompletedTask), bottom);
        Drain();
    }
    [TearDown]
    public void TearDown()
    {
        try
        {
            cancellation.Cancel(); shell.DismissSampleManager(drawer); UiTestPump.Run(showing);
            UiTestPump.Run(editor.DeactivateAsync()); UiTestPump.Run(page.PlaybackInputCompletion); UiTestPump.Run(page.DeactivateAsync());
            shell.Close(); services.Dispose(); cancellation.Dispose();
        }
        finally { SynchronizationContext.SetSynchronizationContext(previousContext); }
    }

    [TestCase(true)][TestCase(false)]
    public void HostModeControlsBlockingWithoutChangingDefaultDrawerContract(bool bottom)
    {
        Show(bottom);
        Assert.That(shell.IsBottomDrawer, Is.EqualTo(bottom));
        Assert.That(shell.IsBlockingDrawerVisible, Is.EqualTo(!bottom));
        Assert.That(shell.IsSampleManagerVisible, Is.True);
        var content = (FrameworkElement)shell.FindName("SampleManagerAppContent");
        var backdrop = (Border)shell.FindName("SampleManagerBackdrop");
        Assert.That(content.IsEnabled, Is.EqualTo(bottom));
        Assert.That(content.Effect is null, Is.EqualTo(bottom));
        Assert.That(backdrop.Visibility, Is.EqualTo(bottom ? Visibility.Collapsed : Visibility.Visible));
        Assert.That(backdrop.IsHitTestVisible, Is.EqualTo(!bottom));
        UiControlContract.AssertStyle<ContentControl>((ContentControl)shell.FindName("SampleManagerHost"), "OHsMainWindowShellDrawerHostStyle");
    }

    [Test]
    public void BottomUnderlayKeepsShortcutsWhileDrawerOriginNeverReachesWorkstation()
    {
        Show(true);
        Assert.That(shell.HandleWindowKeyDown(true, Key.D3, ModifierKeys.Control, false, page), Is.True);
        Assert.That(owner.Subdivision, Is.EqualTo(3));
        Assert.That(shell.HandleWindowKeyDown(true, Key.D4, ModifierKeys.Control, false, drawer), Is.False);
        Assert.That(shell.HandleWindowKeyDown(true, Key.Space, ModifierKeys.None, false, (DependencyObject)drawer.FindName("AddPointButton")), Is.False);
        Assert.That(shell.HandleWindowMouseWheel(true, ModifierKeys.Control, 120, drawer), Is.False);
        Assert.That(shell.HandleWindowMouseWheel(true, ModifierKeys.Control, 120, page), Is.True);
        Assert.That(shell.HandleWindowKeyDown(true, Key.Right, ModifierKeys.None, false, page), Is.True);
        UiTestPump.Run(page.PlaybackInputCompletion);
        Assert.That(owner.Position, Is.GreaterThan(TimeSpan.Zero));
        Assert.That(KeyboardNavigation.GetTabNavigation(drawer), Is.EqualTo(KeyboardNavigationMode.Continue));
    }

    [TestCase(true)][TestCase(false)]
    public void FocusPolicyRestoresOwnedFocusAndVetoesCancellation(bool bottom)
    {
        Show(bottom);
        Assert.That(shell.ShouldRestoreDrawerFocus(false, true), Is.True);
        Assert.That(shell.ShouldRestoreDrawerFocus(false, false), Is.EqualTo(!bottom));
        Assert.That(shell.ShouldRestoreDrawerFocus(true, true), Is.False);
    }

    [TestCase(true)][TestCase(false)]
    public void ClosingKeepsModeUntilExitThenResetsForNextPresentation(bool bottom)
    {
        var exit = new TaskCompletionSource();
        Show(bottom, (opening, token) => opening ? Task.CompletedTask : exit.Task.WaitAsync(token));
        var close = shell.RequestSampleManagerCloseAsync(drawer); Drain();
        Assert.That(shell.SampleManagerPhase, Is.EqualTo(OHsSampleDrawerPhase.Closing));
        Assert.That(shell.IsBottomDrawer, Is.EqualTo(bottom)); Assert.That(drawer.IsEnabled, Is.False);
        exit.SetResult(); UiTestPump.Run(close); UiTestPump.Run(showing);
        Assert.That(shell.IsSampleManagerVisible, Is.False); Assert.That(shell.IsBlockingDrawerVisible, Is.False);
        Assert.That(shell.IsBottomDrawer, Is.False);
        Show(!bottom);
        Assert.That(shell.IsBottomDrawer, Is.EqualTo(!bottom));
        Assert.That(shell.IsBlockingDrawerVisible, Is.EqualTo(bottom));
    }

    [TestCase(true)][TestCase(false)]
    public void CancellationBoundsIgnoredEntranceAndLateCompletionCannotResetNewMode(bool bottom)
    {
        var late = new TaskCompletionSource(); Show(bottom, (_, _) => late.Task);
        cancellation.Cancel(); UiTestPump.Run(showing);
        Assert.That(shell.IsSampleManagerVisible, Is.False);
        cancellation.Dispose(); cancellation = new();
        Show(!bottom); late.SetResult(); Drain();
        Assert.That(shell.IsBottomDrawer, Is.EqualTo(!bottom)); Assert.That(showing.IsCompleted, Is.False);
        Assert.That(shell.RunSampleManagerAnimationAsync(true, false, default).IsCompletedSuccessfully, Is.True);
        Assert.That(shell.RunSampleManagerAnimationAsync(false, false, default).IsCompletedSuccessfully, Is.True);
    }
}
