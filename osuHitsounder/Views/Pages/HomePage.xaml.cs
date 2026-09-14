using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.Input;
using osuHitsounder.Resources.CustomControls;
using osuHitsounder.ViewModels.Shell.HomePage;

namespace osuHitsounder.Views.Pages;

public partial class HomePage : UserControl
{
    private readonly HomePageVM viewModel;
    private DispatcherOperation? queuedCheck;
    private Task demand = Task.CompletedTask;
    private bool attached;
    private bool checkAgain;
    private bool songsScrollInput;

    public HomePage(HomePageVM viewModel)
    {
        this.viewModel = viewModel;
        InitializeComponent();
        DataContext = viewModel;
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    public string? FatalError => viewModel.FatalError;
    public bool RequiresShutdown => viewModel.RequiresShutdown;
    public Task LoadAsync(CancellationToken cancellationToken) => viewModel.LoadAsync(cancellationToken);
    public void CancelProjectCreation() => viewModel.CreateProjectCommand.Cancel();
    public void Deactivate()
    {
        Detach();
        viewModel.Deactivate();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (attached) return;
        attached = true;
        ProjectsList.AddHandler(ScrollViewer.ScrollChangedEvent, new ScrollChangedEventHandler(OnScrollChanged));
        SongsList.AddHandler(ScrollViewer.ScrollChangedEvent, new ScrollChangedEventHandler(OnScrollChanged));
        SizeChanged += OnSizeChanged;
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
    private void OnStateChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(HomePageVM.HasMoreProjects) or nameof(HomePageVM.HasMoreSongs)
            or nameof(HomePageVM.IsLoadingProjects) or nameof(HomePageVM.IsLoadingSongs)
            or nameof(HomePageVM.CanInteract))
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

    private void Detach()
    {
        if (!attached) return;
        attached = false;
        queuedCheck?.Abort();
        ProjectsList.RemoveHandler(ScrollViewer.ScrollChangedEvent, new ScrollChangedEventHandler(OnScrollChanged));
        SongsList.RemoveHandler(ScrollViewer.ScrollChangedEvent, new ScrollChangedEventHandler(OnScrollChanged));
        SizeChanged -= OnSizeChanged;
        viewModel.PropertyChanged -= OnStateChanged;
    }
}
