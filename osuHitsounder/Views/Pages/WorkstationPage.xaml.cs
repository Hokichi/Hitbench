using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using osuHitsounder.ViewModels.Shell;

namespace osuHitsounder.Views.Pages;

public partial class WorkstationPage : UserControl
{
    private readonly WorkstationPageVM viewModel;
    private readonly DispatcherTimer playbackTimer;
    private string? projectPath;
    private bool loadStarted;
    private bool deactivationStarted;

    public WorkstationPage(WorkstationPageVM viewModel)
    {
        this.viewModel = viewModel;
        InitializeComponent();
        DataContext = viewModel;
        playbackTimer = new DispatcherTimer(DispatcherPriority.Background, Dispatcher) { Interval = TimeSpan.FromMilliseconds(50) };
        playbackTimer.Tick += OnPlaybackTick;
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    public Task LoadCompletion { get; private set; } = Task.CompletedTask;

    public void SetProjectPath(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        if (loadStarted) throw new InvalidOperationException("The workstation has already started loading.");
        projectPath = value;
    }

    public Task DeactivateAsync()
    {
        if (deactivationStarted) return LoadCompletion;
        deactivationStarted = true;
        playbackTimer.Stop();
        LoadCompletion = DeactivateCoreAsync();
        return LoadCompletion;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (loadStarted || projectPath is null) return;
        loadStarted = true;
        LoadCompletion = LoadCoreAsync(projectPath);
        try { await LoadCompletion; }
        catch (OperationCanceledException) { }
    }

    private async Task LoadCoreAsync(string path)
    {
        await viewModel.LoadAsync(path, CancellationToken.None);
        if (!deactivationStarted) playbackTimer.Start();
    }

    private async void OnUnloaded(object sender, RoutedEventArgs e)
    {
        try { await DeactivateAsync(); }
        catch (OperationCanceledException) { }
    }

    private async Task DeactivateCoreAsync()
    {
        try { await viewModel.DeactivateAsync(); }
        finally
        {
            playbackTimer.Tick -= OnPlaybackTick;
            Loaded -= OnLoaded;
            Unloaded -= OnUnloaded;
        }
    }

    private void OnPlaybackTick(object? sender, EventArgs e) => viewModel.RefreshPlayback();
    private void OnWaveformViewportSizeChanged(object sender, SizeChangedEventArgs e) => WaveformDisplay.ViewportWidth = Math.Max(0, e.NewSize.Width);
    private void OnWaveformScrollChanged(object sender, ScrollChangedEventArgs e) => WaveformDisplay.HorizontalOffset = e.HorizontalOffset;
    private void OnFitClicked(object sender, RoutedEventArgs e) => WaveformScrollViewer.ScrollToHorizontalOffset(0);

    private void OnWaveformKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Left: WaveformScrollViewer.LineLeft(); break;
            case Key.Right: WaveformScrollViewer.LineRight(); break;
            case Key.Home: WaveformScrollViewer.ScrollToLeftEnd(); break;
            case Key.End: WaveformScrollViewer.ScrollToRightEnd(); break;
            default: return;
        }
        e.Handled = true;
    }
}
