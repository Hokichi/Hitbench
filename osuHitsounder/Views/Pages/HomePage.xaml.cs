using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.Input;
using osuHitsounder.ViewModels.Shell.HomePage;

namespace osuHitsounder.Views.Pages;

public partial class HomePage : UserControl
{
    private readonly HomePageVM viewModel;
    private DispatcherOperation? queuedCheck;
    private Task demand = Task.CompletedTask;
    private bool attached;
    private bool checkAgain;

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
    private void OnScrollChanged(object sender, ScrollChangedEventArgs e) => QueueCheck();
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
                await CheckListAsync(ProjectsList, viewModel.LoadMoreProjectsCommand);
                await CheckListAsync(SongsList, viewModel.LoadMoreSongsCommand);
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

    private async Task CheckListAsync(ItemsControl list, IAsyncRelayCommand command)
    {
        if (!attached || !command.CanExecute(null)) return;
        if (list.Template.FindName("PART_ScrollViewer", list) is not ScrollViewer viewer) return;
        if (viewer.ViewportHeight > 0 &&
            viewer.ExtentHeight - viewer.VerticalOffset - viewer.ViewportHeight <= viewer.ViewportHeight)
            await command.ExecuteAsync(null);
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
