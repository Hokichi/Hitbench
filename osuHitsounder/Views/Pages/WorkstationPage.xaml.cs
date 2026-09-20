using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using System.ComponentModel;
using osuHitsounder.ViewModels.Shell;

namespace osuHitsounder.Views.Pages;

public partial class WorkstationPage : UserControl
{
    private readonly WorkstationPageVM viewModel;
    private readonly DispatcherTimer playbackTimer;
    private string? projectPath;
    private bool loadStarted;
    private bool deactivationStarted;
    private bool hasPendingZoomAnchor;
    private double zoomAnchorFraction;
    private double zoomAnchorViewportX;

    public WorkstationPage(WorkstationPageVM viewModel)
    {
        this.viewModel = viewModel;
        InitializeComponent();
        DataContext = viewModel;
        playbackTimer = new DispatcherTimer(DispatcherPriority.Background, Dispatcher) { Interval = TimeSpan.FromMilliseconds(50) };
        playbackTimer.Tick += OnPlaybackTick;
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        viewModel.PropertyChanging += OnViewModelPropertyChanging;
        viewModel.PropertyChanged += OnViewModelPropertyChanged;
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
            viewModel.PropertyChanging -= OnViewModelPropertyChanging;
            viewModel.PropertyChanged -= OnViewModelPropertyChanged;
            hasPendingZoomAnchor = false;
        }
    }

    private void OnPlaybackTick(object? sender, EventArgs e) => viewModel.RefreshPlayback();
    private void OnWaveformViewportSizeChanged(object sender, SizeChangedEventArgs e) => WaveformDisplay.ViewportWidth = Math.Max(0, e.NewSize.Width);
    private void OnWaveformDisplaySizeChanged(object sender, SizeChangedEventArgs e) => ApplyPendingZoomAnchor();
    private void OnWaveformScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        WaveformDisplay.HorizontalOffset = e.HorizontalOffset;
        ApplyPendingZoomAnchor();
    }

    private void OnViewModelPropertyChanging(object? sender, PropertyChangingEventArgs e)
    {
        if (e.PropertyName != nameof(WorkstationPageVM.ZoomFactor) || hasPendingZoomAnchor) return;
        var duration = viewModel.Waveform?.Duration.TotalSeconds ?? 0;
        var extent = WaveformDisplay.ActualWidth;
        var viewport = WaveformScrollViewer.ViewportWidth;
        if (!double.IsFinite(duration) || duration <= 0 || !double.IsFinite(extent) || extent <= 0 ||
            !double.IsFinite(viewport) || viewport <= 0) return;

        zoomAnchorFraction = Math.Clamp(viewModel.Position.TotalSeconds / duration, 0, 1);
        var markerViewportX = zoomAnchorFraction * extent - WaveformScrollViewer.HorizontalOffset;
        zoomAnchorViewportX = markerViewportX >= 0 && markerViewportX <= viewport
            ? markerViewportX
            : viewport / 2;
        hasPendingZoomAnchor = true;
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(WorkstationPageVM.ZoomFactor)) ApplyPendingZoomAnchor();
    }

    private void ApplyPendingZoomAnchor()
    {
        if (!hasPendingZoomAnchor || deactivationStarted) return;
        var extent = WaveformDisplay.ActualWidth;
        var viewport = WaveformScrollViewer.ViewportWidth;
        if (!double.IsFinite(extent) || extent <= 0 || !double.IsFinite(viewport) || viewport <= 0) return;
        var expectedExtent = WaveformDisplay.ViewportWidth * viewModel.ZoomFactor;
        if (Math.Abs(extent - expectedExtent) > 1 || Math.Abs(WaveformScrollViewer.ExtentWidth - extent) > 1) return;

        var maximum = Math.Max(0, extent - viewport);
        var offset = viewModel.ZoomFactor <= 1
            ? 0
            : Math.Clamp(zoomAnchorFraction * extent - zoomAnchorViewportX, 0, maximum);
        hasPendingZoomAnchor = false;
        WaveformScrollViewer.ScrollToHorizontalOffset(offset);
    }

    private void OnZoomEditorMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (ZoomEditor.IsKeyboardFocusWithin) return;
        ZoomEditor.Focus();
        ZoomEditor.SelectAll();
        e.Handled = true;
    }

    private void OnZoomEditorGotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e) => ZoomEditor.SelectAll();

    private void OnZoomEditorLostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (viewModel.CommitZoomCommand.CanExecute(null)) viewModel.CommitZoomCommand.Execute(null);
    }

    private void OnZoomEditorKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            if (viewModel.CommitZoomCommand.CanExecute(null)) viewModel.CommitZoomCommand.Execute(null);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            viewModel.CancelZoomEditCommand.Execute(null);
            e.Handled = true;
        }
    }

    private void OnWaveformMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        WaveformScrollViewer.Focus();
        if (!WaveformDisplay.SeekAt(e.GetPosition(WaveformDisplay).X)) return;
        e.Handled = true;
    }

    private void OnWaveformKeyDown(object sender, KeyEventArgs e)
    {
        if (Keyboard.Modifiers == ModifierKeys.Control)
        {
            var duration = viewModel.Waveform?.Duration;
            if (duration is null) return;
            var target = e.Key switch
            {
                Key.Left => viewModel.Position - TimeSpan.FromSeconds(1),
                Key.Right => viewModel.Position + TimeSpan.FromSeconds(1),
                Key.Home => TimeSpan.Zero,
                Key.End => duration.Value,
                _ => (TimeSpan?)null
            };
            if (target is null || !viewModel.SeekCommand.CanExecute(target.Value)) return;
            viewModel.SeekCommand.Execute(target.Value);
            e.Handled = true;
            return;
        }
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
