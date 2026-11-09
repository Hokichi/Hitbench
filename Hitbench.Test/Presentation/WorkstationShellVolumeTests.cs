using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Messaging;
using Hitbench.Resources.DataModels;
using Hitbench.Resources.Messages;
using Hitbench.ViewModels.Popups;
using Hitbench.ViewModels.Shell;
using Hitbench.ViewModels.Shell.HomePage;
using Hitbench.Views.Pages;
using Hitbench.Views.Popups;

namespace Hitbench.Test.Presentation;

[Apartment(ApartmentState.STA), NonParallelizable]
public class WorkstationShellVolumeTests
{
    [TestCase(600, 420, true, 420)]
    [TestCase(300, 420, true, 300)]
    [TestCase(600, 200, true, 200)]
    [TestCase(600, 420, false, 300)]
    [TestCase(0, 420, true, 0)]
    [TestCase(0, 420, false, 0)]
    public void DrawerSizingUsesExplicitContentAndAvailableSpace(double available, double desired, bool contentSized, double expected)
    {
        Assert.That(WorkstationPage.CalculateBottomDrawerHeight(available, desired, contentSized), Is.EqualTo(expected));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void BottomDrawerCanDetachAndReattachAfterResize(bool mapping) => WithDrawer(mapping, (_, page, _, view) =>
    {
        var slot = (Grid)page.FindName("BottomDrawerSlot");
        var motion = (Grid)slot.Children[0];
        page.DetachDrawerHost(motion);
        Assert.That(slot.Children, Is.Empty);
        page.AttachDrawerHost(motion, mapping ? OHsWorkstationDrawerKind.KeysMapping : OHsWorkstationDrawerKind.Arrangement);
        page.SettleDrawerHostAnimation(motion, true);
        view.Root.Measure(new Size(900, 600));
        view.Root.Arrange(new Rect(0, 0, 900, 600));
        view.Root.UpdateLayout();
        Assert.That(slot.Children, Has.Count.EqualTo(1));
        Assert.That(slot.Children[0], Is.SameAs(motion));
        Assert.That(motion.IsEnabled, Is.True);
    });

    [SetUp]
    public void Setup()
    {
        new SongsResponsiveLayoutTests().SetUp();
        Application.Current.Resources.MergedDictionaries.Add(new ResourceDictionary
        { Source = new Uri("/Hitbench;component/Resources/Styles/PianoRollView.xaml", UriKind.Relative) });
    }

    [TestCase(false)]
    [TestCase(true)]
    public void AltVFromEitherDrawerOpensVolume(bool mapping) => WithDrawer(mapping, (shell, page, drawer, _) =>
    {
        Assert.That(shell.HandleWindowKeyDown(true, Key.V, ModifierKeys.Alt, false, drawer), Is.True);
        Assert.That(page.IsVolumeOverlayOpen, Is.True);
    });

    [TestCase(false)]
    [TestCase(true)]
    public void BottomDrawerRootUsesCardSurface(bool mapping) => WithDrawer(mapping, (_, _, drawer, _) =>
        UiControlContract.AssertStyle<Border>((Border)drawer.Content, mapping ? "CardBorderStyle" : "OHsArrangementCardStyle"));

    [TestCase(false)]
    [TestCase(true)]
    public void BottomDrawerPointerPreservesTrackSelection(bool mapping) => WithDrawer(mapping, (_, page, drawer, view) =>
    {
        var vm = (WorkstationPageVM)page.DataContext;
        var node = new Hitbench.Core.Entities.Sample { Timestamp = 1000, FileName = "a.wav" };
        var track = new Hitbench.Core.Entities.Track { Samples = [node] };
        vm.Tracks.Add(track);
        vm.SelectNode(track, node, false);
        view.UpdateLayout();
        var click = new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left)
            { RoutedEvent = Mouse.PreviewMouseDownEvent };
        ((UIElement)drawer.Content).RaiseEvent(click);
        Assert.That(vm.SelectedTrack, Is.SameAs(track));
        Assert.That(vm.SelectedSamples, Does.Contain(node));
        Assert.That(page.IsRegionSelectionActive, Is.False);
        Assert.That(click.Handled, Is.False);
    });

    [TestCase(false)]
    [TestCase(true)]
    public void BottomDrawerNavigationReleasesPresentation(bool mapping) => WithDrawer(mapping, (shell, page, _, view) =>
    {
        var presenter = (ContentControl)shell.FindName("InstrumentalDrawerHost");
        UiTestPump.Run(shell.ReturnHomeAsync());
        view.UpdateLayout();
        Assert.That(shell.IsAnyDrawerVisible, Is.False);
        Assert.That(shell.IsBottomDrawer, Is.False);
        Assert.That(presenter.Content, Is.Null);
        Assert.That(page.LoadCompletion.IsCompletedSuccessfully, Is.True);
        Assert.That(page.HasAnimatedProperties, Is.False);
    });

    [TestCase(false)]
    [TestCase(true)]
    public void BottomDrawerAnimationSettlesWithPresentation(bool mapping) => WithDrawer(mapping, (shell, page, _, _) =>
    {
        var entrance = shell.RunDrawerAnimationAsync(shell.InstrumentalDrawerPresentation, true, true, default);
        Assert.That(entrance.IsCompleted, Is.False);
        Assert.That(page.HasAnimatedProperties, Is.True);
        shell.SettleDrawerAnimation(shell.InstrumentalDrawerPresentation);
        UiTestPump.Run(entrance);
        Assert.That(page.HasAnimatedProperties, Is.False);

        using var cancellation = new CancellationTokenSource();
        var interrupted = shell.RunDrawerAnimationAsync(shell.InstrumentalDrawerPresentation, true, true, cancellation.Token);
        cancellation.Cancel();
        Assert.That(() => UiTestPump.Run(interrupted), Throws.InstanceOf<OperationCanceledException>());
        Assert.That(page.HasAnimatedProperties, Is.False);
        var exit = shell.RunDrawerAnimationAsync(shell.InstrumentalDrawerPresentation, false, true, default);
        Assert.That(page.HasAnimatedProperties, Is.True);
        shell.SettleDrawerAnimation(shell.InstrumentalDrawerPresentation);
        UiTestPump.Run(exit);
        Assert.That(page.HasAnimatedProperties, Is.False);
        foreach (var opening in new[] { true, false })
        {
            var immediate = shell.RunDrawerAnimationAsync(shell.InstrumentalDrawerPresentation, opening, false, default);
            Assert.That(immediate.IsCompletedSuccessfully, Is.True);
            Assert.That(page.HasAnimatedProperties, Is.False);
        }
    });

    [TestCase("SongVolumeWheel")]
    [TestCase("ApplicationVolumeWheel")]
    [TestCase("SamplesVolumeWheel")]
    public void MappingTransportLockDoesNotDisableVolumeControls(string wheelName)
    {
        var previousContext = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(new System.Windows.Threading.DispatcherSynchronizationContext());
        OHsKeysMappingTestContext? mapping = null;
        try
        {
            var opening = OHsKeysMappingTestContext.CreateAsync();
            UiTestPump.Run(opening);
            mapping = opening.GetAwaiter().GetResult();
            var page = new WorkstationPage(mapping.Owner);
            using var view = new InMemoryView(new Window { Content = page, Width = 1000, Height = 700 });
            Assert.That(mapping.Owner.IsInstrumentalTransportBlocked, Is.True);
            Assert.That(mapping.Owner.IsInteractionReady, Is.True);
            Assert.That(mapping.Owner.CanPlay, Is.False);
            page.ShowVolumeOverlay(false, default, false, false);
            var wheel = (Hitbench.Resources.CustomControls.VolumeWheel)page.FindName(wheelName);
            Assert.That(wheel.IsEnabled, Is.True);
            page.ActivateVolumeWheel(wheel);
            Assert.That(page.HandleVolumeOverlayKey(Key.Down, ModifierKeys.None), Is.True);
            Assert.That(mapping.Owner.SongVolume, Is.EqualTo(wheelName == "SongVolumeWheel" ? 95 : 100));
            Assert.That(mapping.Owner.ApplicationVolume, Is.EqualTo(wheelName == "ApplicationVolumeWheel" ? 95 : 100));
            Assert.That(mapping.Owner.SamplesVolume, Is.EqualTo(wheelName == "SamplesVolumeWheel" ? 95 : 100));
            Assert.That(mapping.Owner.PlayPauseCommand.CanExecute(null), Is.False);
            UiTestPump.Run(page.DeactivateAsync());
            Assert.That(wheel.IsEnabled, Is.False);
        }
        finally
        {
            try { if (mapping is not null) UiTestPump.Run(mapping.DisposeAsync().AsTask()); }
            finally { SynchronizationContext.SetSynchronizationContext(previousContext); }
        }
    }

    [TestCase(false)]
    [TestCase(true)]
    public void AltWheelFromEitherDrawerControlsMaster(bool mapping) => WithDrawer(mapping, (shell, page, drawer, _) =>
    {
        var vm = (WorkstationPageVM)page.DataContext;
        Assert.That(shell.HandleWindowMouseWheel(true, ModifierKeys.Alt, -120, drawer), Is.True);
        Assert.That(vm.ApplicationVolume, Is.EqualTo(100));
        Assert.That(shell.HandleWindowMouseWheel(true, ModifierKeys.Alt, -120, drawer), Is.True);
        Assert.That(vm.ApplicationVolume, Is.EqualTo(95));
        Assert.That(vm.SongVolume, Is.EqualTo(100));
        Assert.That(vm.SamplesVolume, Is.EqualTo(100));
    });

    [TestCase(false)]
    [TestCase(true)]
    public void OpenVolumeConsumesDrawerCommands(bool mapping) => WithDrawer(mapping, (shell, page, drawer, _) =>
    {
        var vm = (WorkstationPageVM)page.DataContext;
        page.ShowVolumeOverlay(false, default, true, false);
        foreach (var fading in new[] { false, true })
        {
            if (fading) page.ProcessVolumeOverlayIdle(TimeSpan.FromMilliseconds(100), true);
            foreach (var key in new[] { Key.Space, Key.Delete, Key.F2, Key.Enter })
                Assert.That(shell.HandleWindowKeyDown(true, key, ModifierKeys.None, false, drawer), Is.True);
            Assert.That(vm.IsPlaying, Is.False);
            Assert.That(((ContentControl)shell.FindName("InstrumentalDrawerHost")).Content, Is.SameAs(drawer));
            if (drawer.DataContext is InstrumentalArrangementDrawerVM piano) Assert.That(piano.Notes, Is.Empty);
            if (drawer.DataContext is KeysMappingDrawerVM keys) Assert.That(keys.Rows.Any(row => row.IsMapped), Is.False);
        }
        Assert.That(shell.HandleWindowKeyDown(true, Key.Escape, ModifierKeys.None, false, drawer), Is.True);
        Assert.That(page.IsVolumeOverlayOpen, Is.False);
        Assert.That(shell.IsAnyDrawerVisible, Is.True);
    });

    [Test]
    public void ShellVolumeDisablesUnderlyingContentOnly() => WithDrawer(false, (shell, page, drawer, _) =>
    {
        var backdrop = (Border)shell.FindName("VolumeBackdropHost");
        page.ShowVolumeOverlay(false, default, false, false);
        Assert.That(backdrop.IsEnabled, Is.False);
        Assert.That(backdrop.IsHitTestVisible, Is.False);
        Assert.That(drawer.IsEnabled, Is.False);
        Assert.That(page.ApplicationVolumeWheel.IsEnabled, Is.True);
        Assert.That(page.ApplicationVolumeWheel.IsHitTestVisible, Is.True);
        var owner = (UIElement)backdrop.Parent;
        owner.IsEnabled = false;
        page.DismissVolumeOverlay();
        Assert.That(backdrop.IsEnabled, Is.False);
        Assert.That(backdrop.IsHitTestVisible, Is.True);
        owner.ClearValue(UIElement.IsEnabledProperty);
        Assert.That(drawer.IsEnabled, Is.True);
    });

    [TestCase(false)]
    [TestCase(true)]
    public void HostedVolumeRespectsAccessibilitySettings(bool highContrast) => WithDrawer(true, (shell, page, _, _) =>
    {
        var backdrop = (Border)shell.FindName("VolumeBackdropHost");
        page.ShowVolumeOverlay(false, default, false, highContrast);
        Assert.That(backdrop.Effect is null, Is.EqualTo(highContrast));
        Assert.That(((Grid)page.FindName("WorkstationContent")).Effect, Is.Null);
        Assert.That(page.HeaderBlurEffect, Is.Null);
        Assert.That(backdrop.IsEnabled, Is.False);
        Assert.That(page.VolumeOverlay.HasAnimatedProperties, Is.False);
        page.DismissVolumeOverlay();
        Assert.That(backdrop.Effect, Is.Null);
        Assert.That(backdrop.IsEnabled, Is.True);
    });

    [Test]
    public void OpenVolumePreventsBackgroundWindowActions() => WithDrawer(false, (shell, page, _, _) =>
    {
        page.ShowVolumeOverlay(false, default, false, false);
        Assert.That(shell.HandleBackgroundMouseDown((Border)shell.FindName("ShellRoot"), 1, true,
            new Point(50, 50), new Rect(0, 0, 1280, 720), 1, 1), Is.False);
    });

    [Test]
    public void PianoViewportTracksWorkstationAfterResize() => WithDrawer(false, (shell, page, drawer, view) =>
    {
        var piano = (OHsInstrumentalArrangementDrawer)drawer;
        var roll = (Hitbench.Resources.CustomControls.PianoRollView)piano.FindName("PianoRoll");
        foreach (var width in new[] { 1280d, 900d, 640d, 1100d })
        {
            shell.Width = width;
            view.UpdateLayout();
            view.UpdateLayout();
            var expected = page.Timeline.GetTimeViewportBounds(piano);
            var actual = roll.TransformToVisual(piano).TransformBounds(new Rect(roll.KeyColumnWidth, 0, roll.TimeViewportWidth, 1));
            Assert.That(expected.IsEmpty, Is.False);
            Assert.That(actual.Left, Is.EqualTo(expected.Left).Within(.001));
            Assert.That(actual.Width, Is.EqualTo(expected.Width).Within(.001));
        }
    });

    [Test]
    public void StaticMarkerAndSignedViewportReachAttachedPianoAndDetachCleanly() => WithDrawer(false, (shell, page, drawer, view) =>
    {
        var piano = (OHsInstrumentalArrangementDrawer)drawer;
        var roll = (Hitbench.Resources.CustomControls.PianoRollView)piano.FindName("PianoRoll");
        page.MarkerModeTransitionDelay = (_, _) => Task.CompletedTask;
        UiTestPump.Run(page.ChangeMarkerModeAsync(true)); view.UpdateLayout(); view.UpdateLayout();
        Assert.That(piano.IsPlaybackMarkerStatic, Is.True);
        Assert.That(roll.IsPlaybackMarkerStatic, Is.True);
        Assert.That(roll.HorizontalScrollPadding, Is.EqualTo(page.Timeline.TimelineEdgePadding));
        Assert.That(roll.HorizontalOffset, Is.EqualTo(page.Timeline.TimelineHorizontalOffset));
        Assert.That(roll.HorizontalOffset, Is.LessThan(0));
        var position = ((WorkstationPageVM)page.DataContext).Position;
        roll.PrepareDrawing(); var builds = roll.ContentBuildCount; var offset = page.TimelineHorizontalOffset;
        for (var i = 1; i <= 50; i++)
        {
            page.Timeline.ScrollToHorizontalOffset(offset + i / 4d); roll.PrepareDrawing();
            Assert.That(roll.HorizontalOffset, Is.EqualTo(page.Timeline.TimelineHorizontalOffset));
        }
        Assert.That(roll.ContentBuildCount, Is.EqualTo(builds));
        Assert.That(((WorkstationPageVM)page.DataContext).Position, Is.EqualTo(position), "programmatic follow must not seek through attached piano");
        Assert.That(shell.HandleWindowKeyDown(true, Key.F8, ModifierKeys.None, false, roll), Is.True);
        UiTestPump.Run(page.MarkerModeChangeCompletion); Assert.That(piano.IsPlaybackMarkerStatic, Is.False);
        UiTestPump.Run(shell.ReturnHomeAsync());
        Assert.That(BindingOperations.IsDataBound(piano, OHsInstrumentalArrangementDrawer.TimelineEdgePaddingProperty), Is.False);
        Assert.That(BindingOperations.IsDataBound(piano, OHsInstrumentalArrangementDrawer.IsPlaybackMarkerStaticProperty), Is.False);
    });

    [Test]
    public void VolumeHostDetachesOnNavigationAndClose() => WithDrawer(false, (shell, page, _, _) =>
    {
        var host = (Grid)shell.FindName("ShellVolumeOverlayHost");
        var backdrop = (Border)shell.FindName("VolumeBackdropHost");
        page.ShowVolumeOverlay(false, default, false, false);
        UiTestPump.Run(shell.ReturnHomeAsync());
        Assert.That(page.IsVolumeOverlayOpen, Is.False);
        Assert.That(host.Children, Is.Empty);
        Assert.That(backdrop.Effect, Is.Null);
        Assert.That(BindingOperations.IsDataBound(backdrop, UIElement.EffectProperty), Is.False);
        shell.Receive(new ProjectOpenRequestedMessage("project.json"));
        Assert.That(host.Children, Has.Count.EqualTo(1));
        page.ShowVolumeOverlay(false, default, false, false);
        Assert.That(backdrop.Effect, Is.Null);
        shell.Close();
        Assert.That(host.Children, Is.Empty);
    });

    private static void WithDrawer(bool mapping, Action<MainWindow, WorkstationPage, UserControl, InMemoryView> test)
    {
        var settings = new StubAppSettingsService();
        var messenger = new WeakReferenceMessenger();
        HomePage CreateHome() => new(new HomePageVM(new StubHomeFolderService(), settings,
            new StubInstallationDialogService(), new ImmediateUiScheduler(),
            new StubProjectFileService((_, _, _) => Task.FromResult("saved.json")), messenger));
        var home = CreateHome();
        var shell = TestShell.Create(home, messenger, WorkstationTestPage.Create,
            (HomePageVM)home.DataContext, settings, createHome: CreateHome);
        shell.Receive(new ProjectOpenRequestedMessage("project.json"));
        using var view = new InMemoryView(shell);
        var page = (WorkstationPage)((ContentControl)shell.FindName("PageHost")).Content;
        UiTestPump.Run(page.LoadCompletion);
        var context = new InstrumentalTrackContext("project.json", null, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        var snapshot = new InstrumentalTrackSnapshot(context, "Piano", 100, new Dictionary<string, string>(), [], null, 0);
        UserControl drawer = mapping
            ? new OHsKeysMappingDrawer(new KeysMappingDrawerVM(snapshot, messenger, new ImmediateUiScheduler(),
                new StubSampleFileService([]), new StubSampleFileDialogService((string?)null)))
            : new OHsInstrumentalArrangementDrawer(new InstrumentalArrangementDrawerVM(
                InstrumentalArrangementTestContext.Create(snapshot, [0], [120], TimeSpan.Zero, 4, 32), messenger, new ImmediateUiScheduler()));
        using var cancellation = new CancellationTokenSource();
        var presentation = drawer is OHsKeysMappingDrawer keys
            ? shell.PresentDrawerAsync(keys, cancellation.Token, (_, _) => Task.CompletedTask, true)
            : shell.PresentDrawerAsync((OHsInstrumentalArrangementDrawer)drawer, cancellation.Token, (_, _) => Task.CompletedTask, true);
        // InMemoryView deliberately detaches the root from Window; supply the open-layer lifecycle explicitly.
        ((Grid)shell.FindName("SampleManagerLayer")).Visibility = Visibility.Visible;
        view.UpdateLayout();
        try { test(shell, page, drawer, view); }
        finally
        {
            cancellation.Cancel();
            UiTestPump.Run(presentation);
            if (drawer.DataContext is KeysMappingDrawerVM keyVM) UiTestPump.Run(keyVM.DeactivateAsync());
            if (drawer.DataContext is InstrumentalArrangementDrawerVM pianoVM) UiTestPump.Run(pianoVM.DeactivateAsync());
            shell.Close();
        }
    }
}
