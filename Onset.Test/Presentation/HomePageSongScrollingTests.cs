using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Messaging;
using Onset.Resources.DataModels;
using Onset.ViewModels.Shell.HomePage;
using Onset.Views.Pages;

namespace Onset.Test.Presentation;

[Apartment(ApartmentState.STA), NonParallelizable]
public class HomePageSongScrollingTests
{
    private HomePageVM vm = null!;
    private HomePage page = null!;
    private Window window = null!;
    private InMemoryView view = null!;
    [SetUp]
    public void SetUp()
    {
        new SongsResponsiveLayoutTests().SetUp();
        vm = new HomePageVM(new StubHomeFolderService
            { Songs = (_, _) => Task.FromResult<FolderSnapshot?>(new FolderSnapshot(Enumerable.Range(0, 500).Select(i =>
                new FolderEntry($@"C:\Songs\{i:D3}", $"{i:D3}", DateTimeOffset.MinValue)))) },
            new StubAppSettingsService(), new StubInstallationDialogService(), new StubProjectCreationDialogService(),
            new ImmediateUiScheduler(), new StubProjectFileService((_, _, _) => Task.FromResult("saved.json")), new WeakReferenceMessenger());
        page = new HomePage(vm);
        window = new Window { Content = page, Width = 1920, Height = 1040 };
        view = new InMemoryView(window);
        page.UpdateSongsLayout(new Size(1920, 1040));
        UiTestPump.Run(page.LoadAsync(CancellationToken.None));
        view.UpdateLayout();
    }
    [TearDown]
    public void TearDown() { view.Dispose(); window.Close(); }

    [Test]
    public void SongsWheelHandlesDeltaExactlyOnceAndFractionalTravel()
    {
        UiTestPump.Run(vm.LoadMoreSongsCommand.ExecuteAsync(300));
        view.UpdateLayout();
        var panel = SongsScrollPerformanceTests.FindPanel(page);
        var version = panel.VerticalScrollRequestVersion;
        Assert.That(page.HandleSongsMouseWheel(-30, 3, false, TimeSpan.Zero), Is.True);
        Assert.That(panel.VerticalOffset, Is.EqualTo(.75 * panel.RowStride).Within(.001));
        Assert.That(panel.VerticalScrollRequestVersion, Is.EqualTo(version + 1));
        page.HandleSongsMouseWheel(-240, 3, false, TimeSpan.Zero);
        Assert.That(panel.VerticalOffset, Is.EqualTo(6.75 * panel.RowStride).Within(.001));
        var list = (ItemsControl)page.FindName("SongsList");
        var wheel = new MouseWheelEventArgs(Mouse.PrimaryDevice, 0, -120) { RoutedEvent = Mouse.PreviewMouseWheelEvent };
        var prior = panel.VerticalOffset;
        list.RaiseEvent(wheel);
        Assert.That(wheel.Handled, Is.True);
        page.ProcessSongScrollFrame(TimeSpan.MaxValue);
        var rows = SystemParameters.WheelScrollLines < 0 ? panel.ViewportHeight : SystemParameters.WheelScrollLines * panel.RowStride;
        Assert.That(panel.VerticalOffset, Is.EqualTo(prior + rows).Within(.001));
    }

    [Test]
    public void ThresholdReachedDuringMotionLoadsOneBatchAndAllowsReversalWhileLoading()
    {
        UiTestPump.Run(vm.LoadMoreSongsCommand.ExecuteAsync(40));
        view.UpdateLayout();
        var panel = SongsScrollPerformanceTests.FindPanel(page);
        var max = panel.ExtentHeight - panel.ViewportHeight;
        panel.SetVerticalOffset(max - panel.RowStride * 2);
        view.UpdateLayout();
        SongsScrollPerformanceTests.Drain();
        var before = vm.Songs.Count;
        bool reversedWhileLoading = false;
        vm.Songs.CollectionChanged += (_, _) =>
        {
            if (reversedWhileLoading || !vm.IsLoadingSongs) return;
            reversedWhileLoading = true;
            var displayed = panel.VerticalOffset;
            page.HandleSongsMouseWheel(120, 3, true, TimeSpan.FromMilliseconds(80));
            page.ProcessSongScrollFrame(TimeSpan.FromMilliseconds(96));
            Assert.That(panel.VerticalOffset, Is.LessThan(displayed));
        };
        page.HandleSongsMouseWheel(-120, 3, true, TimeSpan.Zero);
        view.UpdateLayout();
        SongsScrollPerformanceTests.Drain();
        Assert.That(vm.Songs.Count, Is.EqualTo(before));
        page.ProcessSongScrollFrame(TimeSpan.FromMilliseconds(80));
        view.UpdateLayout();
        UiTestPump.Run(window.Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle).Task);
        if (vm.LoadMoreSongsCommand.ExecutionTask is { } loading) UiTestPump.Run(loading);
        Assert.That(reversedWhileLoading, Is.True);
        Assert.That(vm.Songs.Count, Is.EqualTo(before + panel.ItemsPerViewport));
        page.ProcessSongScrollFrame(TimeSpan.FromMilliseconds(240));
        view.UpdateLayout();
        SongsScrollPerformanceTests.Drain();
        Assert.That(vm.Songs.Count, Is.EqualTo(before + panel.ItemsPerViewport));
    }

    [Test]
    public void ExplicitOffsetAtCurrentPositionCancelsPendingWheelTravel()
    {
        UiTestPump.Run(vm.LoadMoreSongsCommand.ExecuteAsync(300));
        view.UpdateLayout();
        var panel = SongsScrollPerformanceTests.FindPanel(page);
        page.HandleSongsMouseWheel(-120, 3, true, TimeSpan.Zero);
        panel.SetVerticalOffset(panel.VerticalOffset);
        page.ProcessSongScrollFrame(TimeSpan.FromMilliseconds(80));
        Assert.That(panel.VerticalOffset, Is.Zero);
    }

    [Test]
    public void BlockedInteractionStopsExistingWheelTail()
    {
        UiTestPump.Run(vm.LoadMoreSongsCommand.ExecuteAsync(300));
        view.UpdateLayout();
        var panel = SongsScrollPerformanceTests.FindPanel(page);
        panel.SetVerticalOffset(120);
        page.HandleSongsMouseWheel(-120, 3, true, TimeSpan.Zero);
        vm.IsInteractionBlocked = true;
        page.ProcessSongScrollFrame(TimeSpan.FromMilliseconds(80));
        Assert.That(panel.VerticalOffset, Is.EqualTo(120));
        Assert.That(page.HandleSongsMouseWheel(-120, 3, true, TimeSpan.Zero), Is.False);
    }

    [Test]
    public void ViewerKeyboardAndFocusScrollCommandsCancelWheelTail()
    {
        UiTestPump.Run(vm.LoadMoreSongsCommand.ExecuteAsync(300));
        view.UpdateLayout();
        var panel = SongsScrollPerformanceTests.FindPanel(page);
        var list = (ItemsControl)page.FindName("SongsList");
        var viewer = (ScrollViewer)list.Template.FindName("PART_ScrollViewer", list);
        page.HandleSongsMouseWheel(-120, 3, true, TimeSpan.Zero);
        viewer.ScrollToVerticalOffset(12);
        view.UpdateLayout();
        page.ProcessSongScrollFrame(TimeSpan.FromMilliseconds(80));
        Assert.That(panel.VerticalOffset, Is.EqualTo(12));
        page.HandleSongsMouseWheel(-120, 3, true, TimeSpan.Zero);
        viewer.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(viewer)!, 0, Key.PageDown)
        { RoutedEvent = Keyboard.KeyDownEvent });
        view.UpdateLayout();
        var keyboard = panel.VerticalOffset;
        Assert.That(keyboard, Is.GreaterThan(12));
        page.ProcessSongScrollFrame(TimeSpan.FromMilliseconds(80));
        Assert.That(panel.VerticalOffset, Is.EqualTo(keyboard));
        page.HandleSongsMouseWheel(-120, 3, true, TimeSpan.Zero);
        var last = (FrameworkElement)panel.Children[panel.Children.Count - 1];
        panel.MakeVisible(last, new Rect(last.RenderSize));
        view.UpdateLayout();
        var focus = panel.VerticalOffset;
        Assert.That(focus, Is.GreaterThan(keyboard));
        page.ProcessSongScrollFrame(TimeSpan.FromMilliseconds(80));
        Assert.That(panel.VerticalOffset, Is.EqualTo(focus));
    }

    [Test]
    public void CompleteLibraryDoesNotQueueIdleDemandCallbacksForWheelFrames()
    {
        UiTestPump.Run(vm.LoadMoreSongsCommand.ExecuteAsync(1000));
        view.UpdateLayout();
        UiTestPump.Run(window.Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle).Task);
        Assert.That(vm.HasMoreSongs, Is.False);
        Assert.That(vm.HasMoreProjects, Is.False);
        Assert.That(page.HandleSongsMouseWheel(-120, 3, true, TimeSpan.Zero), Is.True);
        Assert.That(page.IsDemandCheckPending, Is.False);
        page.ProcessSongScrollFrame(TimeSpan.FromMilliseconds(80));
        Assert.That(page.IsDemandCheckPending, Is.False);
    }

    [Test]
    public void WrappedHeaderAndRtlChangeCancelMotionWithoutLayoutPrefetch()
    {
        UiTestPump.Run(vm.LoadMoreSongsCommand.ExecuteAsync(300));
        view.UpdateLayout();
        var panel = SongsScrollPerformanceTests.FindPanel(page);
        var before = vm.Songs.Count;
        page.HandleSongsMouseWheel(-120, 3, true, TimeSpan.Zero);
        page.ProcessSongScrollFrame(TimeSpan.FromMilliseconds(40));
        vm.IsSongSearchExpanded = true;
        vm.SongsLoadStatus = new string('W', 400);
        page.FlowDirection = FlowDirection.RightToLeft;
        window.Width = page.MinWidth;
        view.UpdateLayout();
        SongsScrollPerformanceTests.Drain();
        var clamped = panel.VerticalOffset;
        page.ProcessSongScrollFrame(TimeSpan.FromMilliseconds(160));
        Assert.That(panel.VerticalOffset, Is.EqualTo(clamped));
        Assert.That(panel.VerticalOffset, Is.InRange(0, Math.Max(0, panel.ExtentHeight - panel.ViewportHeight)));
        Assert.That(vm.Songs.Count, Is.EqualTo(before));
    }

    [Test]
    public void DirectOffsetsSearchTemplateReplacementAndUnloadCancelWheelTail()
    {
        UiTestPump.Run(vm.LoadMoreSongsCommand.ExecuteAsync(300));
        view.UpdateLayout();
        var panel = SongsScrollPerformanceTests.FindPanel(page);
        page.HandleSongsMouseWheel(-120, 3, true, TimeSpan.Zero);
        panel.SetVerticalOffset(12);
        page.ProcessSongScrollFrame(TimeSpan.FromMilliseconds(80));
        Assert.That(panel.VerticalOffset, Is.EqualTo(12));
        page.HandleSongsMouseWheel(-120, 3, true, TimeSpan.Zero);
        panel.LineDown();
        var keyboardOffset = panel.VerticalOffset;
        page.ProcessSongScrollFrame(TimeSpan.FromMilliseconds(80));
        Assert.That(panel.VerticalOffset, Is.EqualTo(keyboardOffset));
        var list = (ItemsControl)page.FindName("SongsList");
        page.HandleSongsMouseWheel(-120, 3, true, TimeSpan.Zero);
        var template = list.Template;
        list.Template = null;
        list.ApplyTemplate();
        list.Template = template;
        view.UpdateLayout();
        page.ProcessSongScrollFrame(TimeSpan.FromMilliseconds(160));
        var replacement = SongsScrollPerformanceTests.FindPanel(page);
        Assert.That(replacement, Is.Not.SameAs(panel));
        Assert.That(replacement.VerticalOffset, Is.Zero);
        page.HandleSongsMouseWheel(-120, 3, true, TimeSpan.Zero);
        vm.SongSearchText = "499";
        UiTestPump.Run(vm.SearchSongsCommand.ExecutionTask!);
        view.UpdateLayout();
        page.ProcessSongScrollFrame(TimeSpan.FromMilliseconds(160));
        Assert.That(replacement.VerticalOffset, Is.Zero);
        view.UpdateLayout();
        page.Deactivate();
        Assert.That(page.HandleSongsMouseWheel(-120, 3, true, TimeSpan.Zero), Is.False);
        page.ProcessSongScrollFrame(TimeSpan.MaxValue);
        Assert.That(replacement.VerticalOffset, Is.Zero);
    }
}
