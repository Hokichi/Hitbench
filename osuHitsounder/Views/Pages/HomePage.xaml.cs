using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
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
        ImageLoader.Clear();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (attached) return;
        attached = true;
        ProjectsList.AddHandler(ScrollViewer.ScrollChangedEvent, new ScrollChangedEventHandler(OnScrollChanged));
        SongsList.AddHandler(ScrollViewer.ScrollChangedEvent, new ScrollChangedEventHandler(OnScrollChanged));
        SizeChanged += OnSizeChanged;
        IsVisibleChanged += OnVisibilityChanged;
        viewModel.PropertyChanged += OnStateChanged;
        QueueCheck();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e) => Deactivate();
    private void OnScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (ReferenceEquals(e.OriginalSource, FindScrollViewer(SongsList)) && e.VerticalChange != 0)
            songsScrollInput = true;
        QueueCheck();
    }
    private void OnSizeChanged(object sender, SizeChangedEventArgs e) => QueueCheck();
    private void OnVisibilityChanged(object sender, DependencyPropertyChangedEventArgs e) => QueueCheck();
    private void OnStateChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (!viewModel.IsProcessing) presentation.TrySetResult();
        if ((e.PropertyName == nameof(HomePageVM.IsProcessing) && viewModel.IsProcessing) ||
            (e.PropertyName == nameof(HomePageVM.IsAwaitingSongsPresentation) &&
             viewModel.IsAwaitingSongsPresentation))
            HideSongs();
        if (e.PropertyName is nameof(HomePageVM.HasMoreProjects) or nameof(HomePageVM.HasMoreSongs)
            or nameof(HomePageVM.IsLoadingProjects) or nameof(HomePageVM.IsLoadingSongs)
            or nameof(HomePageVM.CanInteract) or nameof(HomePageVM.IsAwaitingSongsPresentation))
            QueueCheck();
    }

    private void QueueCheck()
    {
        if (!attached) return;
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
        if (!attached) return;
        var panel = FindDescendant<SpacedUniformGrid>(SongsList);
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

        if (!songsScrollInput || !panel.IsPrefetchThresholdReached) return;
        songsScrollInput = false;
        var requestedCount = panel.ItemsPerViewport;
        if (viewModel.LoadMoreSongsCommand.CanExecute(requestedCount))
            await viewModel.LoadMoreSongsCommand.ExecuteAsync(requestedCount);
    }

    private static ScrollViewer? FindScrollViewer(ItemsControl list) =>
        list.Template.FindName("PART_ScrollViewer", list) as ScrollViewer;

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
        var decodeWidth = (int)Math.Ceiling(336 * VisualTreeHelper.GetDpi(SongsList).DpiScaleX);
        var preloadTasks = viewModel.Songs
            .Select(song => ImageLoader.LoadAsync(song.BackgroundPath, decodeWidth, token))
            .ToArray();
        await Task.WhenAll(preloadTasks).WaitAsync(token);
        if (!attached || generation != viewModel.SongsPresentationGeneration) return false;

        await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Loaded, token);
        SongsList.UpdateLayout();
        var size = SongsList.RenderSize;
        var backgrounds = Descendants<OHsSongBackground>(SongsList).ToArray();
        var tasks = backgrounds.Select(background => background.LoadingTask).ToArray();
        await Task.WhenAll(tasks).WaitAsync(token);
        await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Render, token);

        var changed = backgrounds.Where((background, index) =>
            !ReferenceEquals(background.LoadingTask, tasks[index])).Any();
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
        queuedCheck?.Abort();
        ProjectsList.RemoveHandler(ScrollViewer.ScrollChangedEvent, new ScrollChangedEventHandler(OnScrollChanged));
        SongsList.RemoveHandler(ScrollViewer.ScrollChangedEvent, new ScrollChangedEventHandler(OnScrollChanged));
        SizeChanged -= OnSizeChanged;
        IsVisibleChanged -= OnVisibilityChanged;
        viewModel.PropertyChanged -= OnStateChanged;
    }
}
