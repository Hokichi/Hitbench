using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Input;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.DependencyInjection;
using Hitbench.Resources.DataModels;
using Hitbench.Services;
using Hitbench.ViewModels.Popups;
using Hitbench.ViewModels.Shell;
using Hitbench.Views.Pages;
using Hitbench.Views.Popups;

namespace Hitbench.Test.Presentation;

[Apartment(ApartmentState.STA), NonParallelizable]
public class ControlPointsBottomHostTests
{
    private ServiceProvider services = null!;
    private MainWindow shell = null!;
    private WorkstationPage page = null!;
    private WorkstationPageVM owner = null!;
    private StubShellDrawer drawer = null!;
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
        owner = new(new StubWorkstationSourceService(), new MarkerPlaybackCoordinator(new StubWorkstationAudioService(), Task.Delay), new ImmediateUiScheduler(), new StubSampleFileService(["a.wav"]));
        page = new(owner); UiTestPump.Run(owner.LoadAsync("project.json", default)); owner.AcknowledgePresentationReady(owner.LoadGeneration);
        ((ContentControl)shell.FindName("PageHost")).Content = page;
        drawer = new(); cancellation = new(); showing = Task.CompletedTask;
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
            UiTestPump.Run(page.PlaybackInputCompletion); UiTestPump.Run(page.DeactivateAsync());
            shell.Close(); services.Dispose(); cancellation.Dispose();
        }
        finally { SynchronizationContext.SetSynchronizationContext(previousContext); }
    }

    [TestCase(true)][TestCase(false)]
    public void BothAxesDockWithoutBlockingWorkstation(bool bottom)
    {
        Show(bottom);
        Assert.That(shell.IsBottomDrawer, Is.EqualTo(bottom));
        Assert.That(shell.IsBlockingDrawerVisible, Is.False);
        Assert.That(shell.IsAnyDrawerVisible, Is.True);
        var content = (FrameworkElement)shell.FindName("SampleManagerAppContent");
        var backdrop = (Border)shell.FindName("SampleManagerBackdrop");
        Assert.That(content.IsEnabled, Is.True);
        Assert.That(backdrop.Visibility, Is.EqualTo(Visibility.Collapsed));
        Assert.That(backdrop.IsHitTestVisible, Is.False);
        UiControlContract.AssertStyle<ContentControl>((ContentControl)shell.FindName(bottom ? "InstrumentalDrawerHost" : "SampleManagerHost"), "OHsMainWindowShellDrawerHostStyle");
    }

    [TestCase(FlowDirection.LeftToRight)]
    [TestCase(FlowDirection.RightToLeft)]
    public void SwitchingSideAndBottomKeepsAuthoritativeTimelineInputs(FlowDirection direction)
    {
        shell.FlowDirection = direction;
        owner.AddSampleTrackCommand.Execute(null);
        using var view = new InMemoryView(shell, scheduleLayout: false);
        var component = (OHsWorkstationTrackList)page.FindName("TrackListComponent");
        foreach (var bottom in new[] { false, true, false })
        {
            Show(bottom); view.UpdateLayout();
            foreach (var width in new[] { 1200d, 640d, 900d })
            {
                shell.Width = width; view.UpdateLayout();
                owner.ZoomInCommand.Execute(null); view.UpdateLayout();
                page.Timeline.ScrollFromUser(125); view.UpdateLayout();
                var grid = WorkstationControlTree.Descendants(page).OfType<Hitbench.Resources.CustomControls.BeatGridView>().Single();
                Assert.That(grid.ContentWidth, Is.EqualTo(page.Timeline.TimelineContentWidth));
                Assert.That(grid.HorizontalOffset, Is.EqualTo(page.Timeline.TimelineHorizontalOffset));
                Assert.That(grid.ViewportWidth, Is.EqualTo(component.TrackViewportWidth));
                Assert.That(shell.IsBlockingDrawerVisible, Is.False);
                Assert.That(owner.IsInteractionReady, Is.True);
            }
            page.ShowVolumeOverlay(false, default, false, false);
            Assert.That(drawer.IsEnabled, Is.False);
            Assert.That(page.ApplicationVolumeWheel.IsEnabled, Is.True);
            page.DismissVolumeOverlay();
            Assert.That(drawer.IsEnabled, Is.True);
            UiTestPump.Run(shell.RequestSampleManagerCloseAsync(drawer)); UiTestPump.Run(showing);
            Assert.That(shell.IsSampleManagerVisible, Is.False);
        }
    }

    [Test]
    public void BottomUnderlayKeepsShortcutsWhileDrawerOriginNeverReachesWorkstation()
    {
        Show(true);
        Assert.That(shell.HandleWindowKeyDown(true, Key.D3, ModifierKeys.Control, false, page), Is.True);
        Assert.That(owner.Subdivision, Is.EqualTo(3));
        Assert.That(shell.HandleWindowKeyDown(true, Key.D4, ModifierKeys.Control, false, drawer), Is.False);
        Assert.That(shell.HandleWindowKeyDown(true, Key.Space, ModifierKeys.None, false, drawer.Action), Is.False);
        Assert.That(shell.HandleWindowMouseWheel(true, ModifierKeys.Control, 120, drawer), Is.False);
        Assert.That(shell.HandleWindowMouseWheel(true, ModifierKeys.Control, 120, page), Is.True);
        Assert.That(shell.HandleWindowKeyDown(true, Key.Right, ModifierKeys.None, false, page), Is.True);
        UiTestPump.Run(page.PlaybackInputCompletion);
        Assert.That(owner.Position, Is.GreaterThan(TimeSpan.Zero));
        Assert.That(KeyboardNavigation.GetTabNavigation(drawer), Is.EqualTo(KeyboardNavigationMode.Continue));
    }

    [Test]
    public void DetachedSamplesetPopupContentDoesNotTriggerWorkstationShortcuts()
    {
        Show(true); UiTestPump.Layout(drawer, 720, 220);
        var combo = Descendants(drawer).OfType<ComboBox>().Single();
        var popup = (Popup)combo.Template.FindName("PART_Popup", combo);
        Assert.That(popup.IsOpen, Is.False);
        // A popup has a separate visual root while retaining its logical owner.
        // Substitute that root without opening the native popup.
        var popupRoot = new ContainerVisual();
        popupRoot.Children.Add((Visual)popup.Child);
        try
        {
            Assert.That(shell.HandleWindowMouseWheel(true, ModifierKeys.Control, 120, popup.Child), Is.False);
            Assert.That(shell.HandleWindowKeyDown(true, Key.Space, ModifierKeys.None, false, popup.Child), Is.False);
        }
        finally { popupRoot.Children.Remove((Visual)popup.Child); }
    }
    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        yield return root;
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
            foreach (var child in Descendants(VisualTreeHelper.GetChild(root, index))) yield return child;
    }

    [TestCase(true)][TestCase(false)]
    public void FocusPolicyRestoresOwnedFocusAndVetoesCancellation(bool bottom)
    {
        Show(bottom);
        Assert.That(shell.ShouldRestoreDrawerFocus(false, true), Is.True);
        Assert.That(shell.ShouldRestoreDrawerFocus(false, false), Is.False);
        Assert.That(shell.ShouldRestoreDrawerFocus(true, true), Is.False);
    }

    [TestCase(true)][TestCase(false)]
    public void ClosingKeepsModeUntilExitThenResetsForNextPresentation(bool bottom)
    {
        var exit = new TaskCompletionSource();
        Show(bottom, (opening, token) => opening ? Task.CompletedTask : exit.Task.WaitAsync(token));
        var close = shell.RequestSampleManagerCloseAsync(drawer); Drain();
        Assert.That(bottom ? shell.InstrumentalDrawerPresentation.Phase : shell.SampleManagerPhase, Is.EqualTo(OHsSampleDrawerPhase.Closing));
        Assert.That(shell.IsBottomDrawer, Is.EqualTo(bottom)); Assert.That(drawer.IsEnabled, Is.False);
        exit.SetResult(); UiTestPump.Run(close); UiTestPump.Run(showing);
        Assert.That(shell.IsAnyDrawerVisible, Is.False); Assert.That(shell.IsBlockingDrawerVisible, Is.False);
        Assert.That(shell.IsBottomDrawer, Is.False);
        Show(!bottom);
        Assert.That(shell.IsBottomDrawer, Is.EqualTo(!bottom));
        Assert.That(shell.IsBlockingDrawerVisible, Is.False);
    }

    [TestCase(true)][TestCase(false)]
    public void CancellationBoundsIgnoredEntranceAndLateCompletionCannotResetNewMode(bool bottom)
    {
        var presenter = (ContentControl)shell.FindName(bottom ? "InstrumentalDrawerHost" : "SampleManagerHost");
        var late = new TaskCompletionSource(); Show(bottom, (_, _) => late.Task);
        cancellation.Cancel(); UiTestPump.Run(showing);
        Assert.That(shell.IsAnyDrawerVisible, Is.False);
        cancellation.Dispose(); cancellation = new();
        Show(!bottom); late.SetResult(); Drain();
        Assert.That(shell.FindName(bottom ? "InstrumentalDrawerHost" : "SampleManagerHost"), Is.SameAs(presenter));
        Assert.That(presenter.Content, Is.Null);
        Assert.That(((ContentControl)shell.FindName(bottom ? "SampleManagerHost" : "InstrumentalDrawerHost")).Content, Is.SameAs(drawer));
        Assert.That(shell.IsBottomDrawer, Is.EqualTo(!bottom)); Assert.That(showing.IsCompleted, Is.False);
        Assert.That(shell.RunSampleManagerAnimationAsync(true, false, default).IsCompletedSuccessfully, Is.True);
        Assert.That(shell.RunSampleManagerAnimationAsync(false, false, default).IsCompletedSuccessfully, Is.True);
    }

    [Test]
    public void FailedBottomEntranceRestoresPresenterForNextSideDrawer()
    {
        var presenter = (ContentControl)shell.FindName("SampleManagerHost");
        Show(true, (_, _) => Task.FromException(new InvalidOperationException("entrance failed")));
        Assert.That(() => UiTestPump.Run(showing), Throws.TypeOf<InvalidOperationException>().With.Message.EqualTo("entrance failed"));
        Assert.That(shell.SampleManagerPhase, Is.EqualTo(OHsSampleDrawerPhase.Closed));
        Assert.That(presenter.Content, Is.Null);
        Show(false);
        Assert.That(shell.FindName("SampleManagerHost"), Is.SameAs(presenter));
        Assert.That(presenter.Content, Is.SameAs(drawer));
        UiTestPump.Run(shell.RequestSampleManagerCloseAsync(drawer));
        UiTestPump.Run(showing);
    }

    [Test]
    public void BottomSideBottomReusesPresenterAndBindings()
    {
        var context = new object();
        drawer.DataContext = context;
        foreach (var bottom in new[] { true, false, true })
        {
            Show(bottom);
            var presenter = (ContentControl)shell.FindName(bottom ? "InstrumentalDrawerHost" : "SampleManagerHost");
            Assert.That(presenter.Content, Is.SameAs(drawer));
            Assert.That(drawer.DataContext, Is.SameAs(context));
            Assert.That(shell.IsBottomDrawer, Is.EqualTo(bottom));
            Assert.That(shell.IsBlockingDrawerVisible, Is.False);
            UiControlContract.AssertStyle<ContentControl>(presenter, "OHsMainWindowShellDrawerHostStyle");
            UiTestPump.Run(shell.RequestSampleManagerCloseAsync(drawer));
            UiTestPump.Run(showing);
            Assert.That(presenter.Content, Is.Null);
        }
    }
}
