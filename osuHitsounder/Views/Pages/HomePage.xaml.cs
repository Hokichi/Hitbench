using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Input;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.Input;
using osuHitsounder.Resources.CustomControls;
using osuHitsounder.ViewModels.Shell.HomePage;
using osuHitsounder.Services.Ui;

namespace osuHitsounder.Views.Pages;

public partial class HomePage : UserControl
{
    private readonly HomePageVM viewModel;
    private DispatcherOperation? queuedCheck;
    private Task demand = Task.CompletedTask;
    private bool attached;
    private bool checkAgain;
    private bool songsScrollInput;
    private long lastScrollRequest;
    private Size maximizedPageSize;
    private SongTileLayout? tileLayout;
    private bool updatingLayout;
    private double baselineHeaderHeight;
    private (Size Page, Size Songs, Size Maximum, double Header, double Lane, double NativeLane,
        Thickness Padding, FlowDirection Direction, object Panel, object? ViewerTemplate,
        Thickness PanelPadding, Thickness Border, Thickness PanelMargin, Thickness PageMargin,
        double ProjectsWidth, double HorizontalGap, double VerticalGap)? lastSongsLayout;
    private readonly SongScrollMotion scrollMotion = new();
    private SpacedUniformGrid? songsPanel;
    private ScrollViewer? songsViewer;
    private ControlTemplate? songsTemplate;
    private bool renderingSubscribed;
    private long motionClockStart;
    private TimeSpan motionTimeStart;
    private (double Width, double Height, double Stride, int Columns, FlowDirection Direction) motionGeometry;
    internal long SongsLayoutCalculationCount { get; private set; }
    internal bool IsDemandCheckPending => queuedCheck?.Status == DispatcherOperationStatus.Pending;
    private readonly CancellationTokenSource lifetime = new();
    private readonly TaskCompletionSource presentation = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public HomePage(HomePageVM viewModel)
    {
        this.viewModel = viewModel;
        InitializeComponent();
        DataContext = viewModel;
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    public string? FatalError => viewModel.FatalError;
    public SongImageLoader ImageLoader { get; } = new();
    public bool RequiresShutdown => viewModel.RequiresShutdown;
    internal bool HandleSongsMouseWheel(int delta, int wheelLines, bool animate, TimeSpan now)
    {
        if (!attached || !viewModel.CanInteract || delta == 0 || !GetSongsParts() || songsPanel!.ViewportHeight <= 0) return false;
        var panel = songsPanel;
        if (scrollMotion.IsActive && panel.VerticalScrollRequestVersion != lastScrollRequest) CancelSongsScrollMotion();
        var offset = scrollMotion.Request(delta, wheelLines, panel.RowStride, panel.ViewportHeight,
            Math.Max(0, panel.ExtentHeight - panel.ViewportHeight), panel.VerticalOffset, now, animate);
        panel.BeginScrollRequest();
        lastScrollRequest = panel.VerticalScrollRequestVersion;
        songsScrollInput = wheelLines != 0;
        motionGeometry = GetMotionGeometry(panel);
        panel.SetAnimatedVerticalOffset(offset);
        motionClockStart = Stopwatch.GetTimestamp();
        motionTimeStart = now;
        if (scrollMotion.IsActive && !renderingSubscribed)
        {
            CompositionTarget.Rendering += OnSongScrollRendering;
            renderingSubscribed = true;
        }
        else if (!scrollMotion.IsActive) StopSongScrollRendering();
        QueueCheck();
        return true;
    }

    internal void ProcessSongScrollFrame(TimeSpan now)
    {
        if (!viewModel.CanInteract)
        {
            CancelSongsScrollMotion();
            return;
        }
        if (!attached || !GetSongsParts() || !scrollMotion.IsActive) return;
        var panel = songsPanel!;
        if (panel.VerticalScrollRequestVersion != lastScrollRequest || GetMotionGeometry(panel) != motionGeometry)
        {
            CancelSongsScrollMotion();
            return;
        }
        panel.SetAnimatedVerticalOffset(scrollMotion.Advance(now, Math.Max(0, panel.ExtentHeight - panel.ViewportHeight)));
        if (!scrollMotion.IsActive) StopSongScrollRendering();
        QueueCheck();
    }

    private static (double, double, double, int, FlowDirection) GetMotionGeometry(SpacedUniformGrid panel) =>
        (panel.ViewportWidth, panel.ViewportHeight, panel.RowStride, panel.Columns, panel.FlowDirection);

    private void OnSongScrollRendering(object? sender, EventArgs e)
    {
        if (!SystemParameters.ClientAreaAnimation || SystemParameters.HighContrast)
            ProcessSongScrollFrame(motionTimeStart + TimeSpan.FromMilliseconds(160));
        else ProcessSongScrollFrame(motionTimeStart + Stopwatch.GetElapsedTime(motionClockStart));
    }

    internal void CancelSongsScrollMotion()
    {
        StopSongScrollRendering();
        scrollMotion.Cancel(songsPanel?.VerticalOffset ?? 0);
        songsScrollInput = false;
    }

    private void StopSongScrollRendering()
    {
        if (!renderingSubscribed) return;
        CompositionTarget.Rendering -= OnSongScrollRendering;
        renderingSubscribed = false;
    }

    private void OnSongsPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (!e.Handled) e.Handled = HandleSongsMouseWheel(e.Delta, SystemParameters.WheelScrollLines,
            SystemParameters.ClientAreaAnimation && !SystemParameters.HighContrast, Stopwatch.GetElapsedTime(0));
    }

    private bool GetSongsParts()
    {
        if (ReferenceEquals(songsTemplate, SongsList.Template) && songsPanel is not null &&
            songsViewer is not null && songsPanel.IsDescendantOf(SongsList)) return true;
        CancelSongsScrollMotion();
        songsTemplate = SongsList.Template;
        songsViewer = FindScrollViewer(SongsList);
        songsPanel = FindDescendant<SpacedUniformGrid>(SongsList);
        lastScrollRequest = songsPanel?.VerticalScrollRequestVersion ?? 0;
        return songsViewer is not null && songsPanel is not null;
    }
    public async Task LoadAsync(CancellationToken cancellationToken)
    {
        try
        {
            await viewModel.LoadAsync(cancellationToken);
            if (!viewModel.IsProcessing) return;
            QueueCheck();
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, lifetime.Token);
            await presentation.Task.WaitAsync(linked.Token);
        }
        catch (OperationCanceledException)
        {
            Deactivate();
            throw;
        }
    }
    public void CancelProjectCreation() => viewModel.CreateProjectCommand.Cancel();
    public void Deactivate()
    {
        Detach();
        lifetime.Cancel();
        presentation.TrySetCanceled(lifetime.Token);
        viewModel.Deactivate();
        HideSongs();
        ReleaseSongImages();
        ImageLoader.Clear();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (attached) return;
        attached = true;
        ProjectsList.AddHandler(ScrollViewer.ScrollChangedEvent, new ScrollChangedEventHandler(OnScrollChanged));
        SongsList.AddHandler(ScrollViewer.ScrollChangedEvent, new ScrollChangedEventHandler(OnScrollChanged));
        SongsList.PreviewMouseWheel += OnSongsPreviewMouseWheel;
        SizeChanged += OnSizeChanged;
        LayoutUpdated += OnLayoutUpdated;
        IsVisibleChanged += OnVisibilityChanged;
        viewModel.PropertyChanged += OnStateChanged;
        QueueCheck();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e) => Deactivate();
    private void OnScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (GetSongsParts() && ReferenceEquals(e.OriginalSource, songsViewer))
        {
            var panel = songsPanel!;
            if (panel.VerticalScrollRequestVersion != lastScrollRequest)
            {
                CancelSongsScrollMotion();
                songsScrollInput = true;
            }
            lastScrollRequest = panel.VerticalScrollRequestVersion;
        }
        QueueCheck();
    }
    private void OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        songsScrollInput = false;
        CancelSongsScrollMotion();
        QueueCheck();
    }

    private void OnLayoutUpdated(object? sender, EventArgs e)
    {
        if (maximizedPageSize.Width > 0) UpdateSongsLayout(maximizedPageSize);
    }

    public void UpdateSongsLayout(Size maximizedSize)
    {
        if (updatingLayout || !double.IsFinite(maximizedSize.Width) || !double.IsFinite(maximizedSize.Height) ||
            maximizedSize.Width <= 0 || maximizedSize.Height <= 0 || SongsList.ActualWidth <= 0 ||
            !GetSongsParts()) return;
        var panel = songsPanel!;
        var viewer = songsViewer!;
        var scrollbar = viewer.Template?.FindName("PART_VerticalScrollBar", viewer) as ScrollBar;
        var lane = scrollbar?.ActualWidth ?? SystemParameters.VerticalScrollBarWidth;
        if (lane <= 0) lane = SystemParameters.VerticalScrollBarWidth;
        var nativeLane = Math.Max(0, SongsList.ActualWidth - panel.ViewportWidth -
            SongsList.Padding.Left - SongsList.Padding.Right);
        lane = Math.Max(lane, nativeLane);
        var headerHeight = SongsContent.RowDefinitions[0].ActualHeight + SongsContent.RowDefinitions[1].ActualHeight +
            SongsContent.RowDefinitions[2].ActualHeight;
        var inputs = (RenderSize, SongsList.RenderSize, maximizedSize, headerHeight, lane, nativeLane,
            SongsList.Padding, FlowDirection, (object)panel, (object?)viewer.Template,
            SongsPanel.Padding, SongsPanel.BorderThickness, SongsPanel.Margin, PageLayout.Margin,
            PageLayout.ColumnDefinitions[0].ActualWidth, panel.HorizontalGap, panel.VerticalGap);
        if (lastSongsLayout == inputs) return;
        lastSongsLayout = inputs;
        CancelSongsScrollMotion();
        updatingLayout = true;
        SongsLayoutCalculationCount++;
        try
        {
            // Native viewers reserve their own lane; the shared overlay viewer needs padding instead.
            var reservedPadding = Math.Max(0, lane - nativeLane);
            var padding = FlowDirection == FlowDirection.RightToLeft
                ? new Thickness(reservedPadding, 0, 0, 0) : new Thickness(0, 0, reservedPadding, 0);
            if (SongsList.Padding != padding) SongsList.Padding = padding;

            var horizontalChrome = SongsPanel.Padding.Left + SongsPanel.Padding.Right +
                SongsPanel.BorderThickness.Left + SongsPanel.BorderThickness.Right;
            var verticalChrome = SongsPanel.Padding.Top + SongsPanel.Padding.Bottom +
                SongsPanel.BorderThickness.Top + SongsPanel.BorderThickness.Bottom;
            var outsideWidth = PageLayout.Margin.Left + PageLayout.Margin.Right +
                PageLayout.ColumnDefinitions[0].ActualWidth + SongsPanel.Margin.Left + SongsPanel.Margin.Right;
            var outsideHeight = PageLayout.Margin.Top + PageLayout.Margin.Bottom +
                SongsPanel.Margin.Top + SongsPanel.Margin.Bottom;
            if (tileLayout is null || maximizedPageSize != maximizedSize ||
                (Math.Abs(ActualWidth - maximizedSize.Width) < .1 && Math.Abs(baselineHeaderHeight - headerHeight) > .01))
            {
                var width = maximizedSize.Width - outsideWidth - horizontalChrome - lane;
                var height = maximizedSize.Height - outsideHeight - verticalChrome - headerHeight;
                if (width <= 4 * panel.HorizontalGap || height <= 7 * panel.VerticalGap) return;
                tileLayout = new SongTileLayout(width, height, panel.HorizontalGap, panel.VerticalGap);
                baselineHeaderHeight = headerHeight;
                maximizedPageSize = maximizedSize;
            }
            var viewportWidth = Math.Max(tileLayout.MinimumViewportWidth, SongsList.ActualWidth - lane);
            SetLayoutValue(panel, SpacedUniformGrid.ColumnsProperty, tileLayout.GetColumns(viewportWidth));
            SetLayoutValue(panel, SpacedUniformGrid.ItemWidthProperty, tileLayout.GetTileWidth(viewportWidth));
            SetLayoutValue(panel, SpacedUniformGrid.ItemHeightProperty, tileLayout.TileHeight);
            SetLayoutValue(SongsPanel, MinWidthProperty, tileLayout.MinimumViewportWidth + horizontalChrome + lane);
            SetLayoutValue(SongsPanel, MinHeightProperty, tileLayout.MinimumViewportHeight + verticalChrome + headerHeight);
            SetLayoutValue(this, MinWidthProperty, outsideWidth + SongsPanel.MinWidth);
            SetLayoutValue(this, MinHeightProperty, outsideHeight + SongsPanel.MinHeight);
        }
        finally { updatingLayout = false; }
    }

    private static void SetLayoutValue(DependencyObject target, DependencyProperty property, object value)
    {
        if (!Equals(target.GetValue(property), value)) target.SetCurrentValue(property, value);
    }
    private void OnVisibilityChanged(object sender, DependencyPropertyChangedEventArgs e) => QueueCheck();
    private void OnStateChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(HomePageVM.CanInteract) && !viewModel.CanInteract)
            CancelSongsScrollMotion();
        if (e.PropertyName == nameof(HomePageVM.SongsInventoryGeneration))
        {
            CancelSongsScrollMotion();
            ReleaseSongImages();
            ImageLoader.Clear();
        }
        if (e.PropertyName == nameof(HomePageVM.SongsPresentationGeneration))
        {
            CancelSongsScrollMotion();
            songsScrollInput = false;
            FindScrollViewer(SongsList)?.ScrollToTop();
            HideSongs();
        }
        if (e.PropertyName == nameof(HomePageVM.IsSongSearchExpanded))
        {
            var expanded = viewModel.IsSongSearchExpanded;
            Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() =>
            {
                if (!attached || lifetime.IsCancellationRequested || expanded != viewModel.IsSongSearchExpanded) return;
                if (expanded) SongSearchTextBox.Focus();
                else SongSearchButton.Focus();
            }));
        }
        if (!viewModel.IsProcessing) presentation.TrySetResult();
        if ((e.PropertyName == nameof(HomePageVM.IsProcessing) && viewModel.IsProcessing) ||
            (e.PropertyName == nameof(HomePageVM.IsAwaitingSongsPresentation) &&
             viewModel.IsAwaitingSongsPresentation))
            HideSongs();
        if (e.PropertyName is nameof(HomePageVM.HasMoreProjects) or nameof(HomePageVM.HasMoreSongs)
            or nameof(HomePageVM.IsLoadingProjects) or nameof(HomePageVM.IsLoadingSongs)
            or nameof(HomePageVM.CanInteract) or nameof(HomePageVM.IsAwaitingSongsPresentation)
            or nameof(HomePageVM.IsSearchingSongs))
            QueueCheck();
    }

    private void OnSongSearchKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape || !viewModel.CloseSongSearchCommand.CanExecute(null)) return;
        viewModel.CloseSongSearchCommand.Execute(null);
        e.Handled = true;
    }

    private void QueueCheck()
    {
        if (!attached || (!viewModel.IsAwaitingSongsPresentation && !viewModel.IsProcessing &&
            !viewModel.HasMoreSongs && !viewModel.HasMoreProjects)) return;
        if (!demand.IsCompleted) { checkAgain = true; return; }
        if (queuedCheck?.Status == DispatcherOperationStatus.Pending) return;
        queuedCheck = Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
        {
            if (attached) demand = CheckDemandAsync();
        }));
    }

    private async Task CheckDemandAsync()
    {
        try
        {
            do
            {
                checkAgain = false;
                await CheckProjectsAsync();
                await CheckSongsAsync();
                if (attached) await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Background);
            } while (attached && checkAgain);
        }
        catch (OperationCanceledException) { }
        catch (Exception error)
        {
            Trace.TraceError("Folder viewport loading failed: {0}", error);
            viewModel.ReportDemandFailure(error);
        }
    }

    private async Task CheckProjectsAsync()
    {
        if (!attached || !viewModel.LoadMoreProjectsCommand.CanExecute(null)) return;
        if (FindScrollViewer(ProjectsList) is not { } viewer) return;
        if (viewer.ViewportHeight > 0 &&
            viewer.ExtentHeight - viewer.VerticalOffset - viewer.ViewportHeight <= viewer.ViewportHeight)
            await viewModel.LoadMoreProjectsCommand.ExecuteAsync(null);
    }

    private async Task CheckSongsAsync()
    {
        if (!attached || viewModel.IsSearchingSongs) return;
        var panel = GetSongsParts() ? songsPanel : null;
        if (panel is null) return;

        if (viewModel.IsAwaitingSongsPresentation)
        {
            if (viewModel.IsLoading || !IsVisible || SongsList.ActualHeight <= 0) return;
            var generation = viewModel.SongsPresentationGeneration;
            await viewModel.LoadInitialSongsAsync(lifetime.Token);
            if (!await PrepareInitialSongsAsync(generation, lifetime.Token))
            {
                checkAgain = true;
                return;
            }
            viewModel.CompleteSongsPresentation(generation);
            RevealSongs(generation);
            return;
        }

        if (viewModel.Songs.Count == 0)
        {
            var panelHeight = panel.ViewportHeight > 0 ? panel.ViewportHeight : panel.ActualHeight;
            if (panelHeight <= 0) panelHeight = SongsList.ActualHeight;
            if (panelHeight <= 0) return;
            var visibleRows = Math.Max(1,
                (int)Math.Floor((panelHeight + panel.VerticalGap) / panel.RowStride));
            var initialCount = checked((visibleRows + 1) * Math.Max(1, panel.Columns));
            if (viewModel.LoadMoreSongsCommand.CanExecute(initialCount))
                await viewModel.LoadMoreSongsCommand.ExecuteAsync(initialCount);
            return;
        }

        if (!songsScrollInput) return;
        if (!panel.IsPrefetchThresholdReached)
        {
            if (!scrollMotion.IsActive) songsScrollInput = false;
            return;
        }
        var requestedCount = panel.ItemsPerViewport;
        if (viewModel.LoadMoreSongsCommand.CanExecute(requestedCount))
        {
            songsScrollInput = false;
            await viewModel.LoadMoreSongsCommand.ExecuteAsync(requestedCount);
        }
    }

    private static ScrollViewer? FindScrollViewer(ItemsControl list) =>
        list.Template?.FindName("PART_ScrollViewer", list) as ScrollViewer;

    private static T? FindDescendant<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            if (child is T match) return match;
            if (FindDescendant<T>(child) is { } descendant) return descendant;
        }
        return null;
    }

    private static IEnumerable<T> Descendants<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T match) yield return match;
            foreach (var descendant in Descendants<T>(child)) yield return descendant;
        }
    }

    private async Task<bool> PrepareInitialSongsAsync(int generation, CancellationToken token)
    {
        var preloadTasks = viewModel.Songs
            .Select(song => ImageLoader.LoadAsync(song.BackgroundPath, SongImageDecodeWidth, token))
            .ToArray();
        await Task.WhenAll(preloadTasks).WaitAsync(token);
        if (!attached || generation != viewModel.SongsPresentationGeneration) return false;

        await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Loaded, token);
        SongsList.UpdateLayout();
        var size = SongsList.RenderSize;
        var backgrounds = Descendants<Image>(SongsList).ToArray();
        foreach (var background in backgrounds)
        {
            if (!imageOwners.ContainsKey(background))
                OnSongImageLoaded(background, new RoutedEventArgs());
            StartSongImage(background);
        }
        if (backgrounds.Any(background => !imageRequests.ContainsKey(background))) return false;
        var tasks = backgrounds.Select(background => imageRequests[background].LoadingTask).ToArray();
        await Task.WhenAll(tasks).WaitAsync(token);
        await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Render, token);

        var changed = backgrounds.Where((background, index) =>
            !imageRequests.TryGetValue(background, out var request) ||
            !ReferenceEquals(request.LoadingTask, tasks[index])).Any();
        return attached && generation == viewModel.SongsPresentationGeneration &&
            size == SongsList.RenderSize && !changed;
    }

    private void RevealSongs(int generation)
    {
        if (!attached || generation != viewModel.SongsPresentationGeneration ||
            viewModel.IsAwaitingSongsPresentation)
            return;

        SongsList.BeginAnimation(OpacityProperty, null);
        SongsList.Opacity = 1;
        if (!SystemParameters.ClientAreaAnimation) return;

        SongsList.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1,
            TimeSpan.FromMilliseconds(160))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            FillBehavior = FillBehavior.Stop
        }, HandoffBehavior.SnapshotAndReplace);
    }

    private void HideSongs()
    {
        SongsList.BeginAnimation(OpacityProperty, null);
        SongsList.Opacity = 0;
    }

    private void Detach()
    {
        if (!attached) return;
        attached = false;
        CancelSongsScrollMotion();
        songsPanel = null;
        songsViewer = null;
        songsTemplate = null;
        lastSongsLayout = null;
        queuedCheck?.Abort();
        ProjectsList.RemoveHandler(ScrollViewer.ScrollChangedEvent, new ScrollChangedEventHandler(OnScrollChanged));
        SongsList.RemoveHandler(ScrollViewer.ScrollChangedEvent, new ScrollChangedEventHandler(OnScrollChanged));
        SongsList.PreviewMouseWheel -= OnSongsPreviewMouseWheel;
        SizeChanged -= OnSizeChanged;
        LayoutUpdated -= OnLayoutUpdated;
        IsVisibleChanged -= OnVisibilityChanged;
        viewModel.PropertyChanged -= OnStateChanged;
    }
}
