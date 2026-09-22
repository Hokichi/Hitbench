using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using System.Windows.Threading;
using System.ComponentModel;
using System.Collections.Specialized;
using osuHitsounder.ViewModels.Shell;
using osuHitsounder.Resources.CustomControls;
using Track = osuHitsounder.Core.Entities.Track;

namespace osuHitsounder.Views.Pages;

public partial class WorkstationPage : UserControl
{
    public static readonly DependencyProperty TimelineContentWidthProperty = DependencyProperty.Register(nameof(TimelineContentWidth), typeof(double), typeof(WorkstationPage));
    public static readonly DependencyProperty TimelineViewportWidthProperty = DependencyProperty.Register(nameof(TimelineViewportWidth), typeof(double), typeof(WorkstationPage));
    public static readonly DependencyProperty TimelineHorizontalOffsetProperty = DependencyProperty.Register(nameof(TimelineHorizontalOffset), typeof(double), typeof(WorkstationPage));
    public double TimelineContentWidth { get => (double)GetValue(TimelineContentWidthProperty); set => SetValue(TimelineContentWidthProperty, value); }
    public double TimelineViewportWidth { get => (double)GetValue(TimelineViewportWidthProperty); set => SetValue(TimelineViewportWidthProperty, value); }
    public double TimelineHorizontalOffset { get => (double)GetValue(TimelineHorizontalOffsetProperty); set => SetValue(TimelineHorizontalOffsetProperty, value); }
    private readonly WorkstationPageVM viewModel;
    private readonly DispatcherTimer playbackTimer;
    private string? projectPath;
    private bool loadStarted;
    private bool deactivationStarted;
    private bool hasPendingZoomAnchor;
    private double zoomAnchorFraction;
    private double zoomAnchorViewportX;
    private ModifierKeys wheelMode;
    private int wheelRemainder;

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
        viewModel.Tracks.CollectionChanged += OnTracksCollectionChanged;
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
        catch (Exception error) { System.Diagnostics.Trace.TraceError("Workstation deactivation failed: {0}", error); }
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
            viewModel.Tracks.CollectionChanged -= OnTracksCollectionChanged;
            hasPendingZoomAnchor = false;
            wheelMode = ModifierKeys.None;
            wheelRemainder = 0;
        }
    }

    private void OnPlaybackTick(object? sender, EventArgs e) => viewModel.RefreshPlayback();
    private void OnWaveformViewportSizeChanged(object sender, SizeChangedEventArgs e)
    {
        WaveformDisplay.ViewportWidth = Math.Max(0, e.NewSize.Width);
        TimelineViewportWidth = WaveformScrollViewer.ViewportWidth;
    }
    private void OnWaveformDisplaySizeChanged(object sender, SizeChangedEventArgs e)
    {
        TimelineContentWidth = e.NewSize.Width;
        ApplyPendingZoomAnchor();
    }
    private void OnWaveformScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        WaveformDisplay.HorizontalOffset = e.HorizontalOffset;
        TimelineHorizontalOffset = e.HorizontalOffset;
        TimelineViewportWidth = e.ViewportWidth;
        WaveformScrollBar.Value = e.HorizontalOffset;
        ApplyPendingZoomAnchor();
    }

    private void OnSampleSelectorLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is ComboBox selector && selector.DataContext is Track track)
        {
            selector.Tag = true;
            var saved = track.Samples.FirstOrDefault()?.FileName;
            selector.SelectedItem = viewModel.SampleFiles.FirstOrDefault(name =>
                string.Equals(name, saved, StringComparison.OrdinalIgnoreCase));
            selector.Tag = false;
            UpdateMissingSampleCue(selector, track);
        }
    }

    private void OnTrackControlDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (sender is ComboBox selector) OnSampleSelectorLoaded(selector, new RoutedEventArgs());
        else if (sender is Slider slider) OnVolumeSliderLoaded(slider, new RoutedEventArgs());
    }

    private void OnSampleSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is ComboBox selector && selector.IsLoaded && selector.Tag is not true && selector.DataContext is Track track)
        {
            if (selector.SelectedItem is string fileName && track.Samples.Any(sample => sample.FileName != fileName))
                viewModel.SelectSample(track, fileName);
            UpdateMissingSampleCue(selector, track);
        }
    }

    private void OnVolumeSliderLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is Slider slider && slider.DataContext is Track track)
        {
            slider.Tag = true;
            slider.Value = track.Volume;
            slider.Tag = false;
        }
    }

    private void OnVolumeValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (sender is Slider slider && slider.IsLoaded && slider.Tag is not true && slider.DataContext is Track track)
            viewModel.SetTrackVolume(track, (int)Math.Round(e.NewValue));
    }

    private async void OnLoadSampleClick(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: Track track }) return;
        var name = await viewModel.ImportSampleAsync(track);
        if (name is null || !IsLoaded || !viewModel.Tracks.Contains(track)) return;
        var container = ItemsControl.ContainerFromElement(TrackList, (DependencyObject)sender) as ListBoxItem;
        if (container?.DataContext != track) return;
        var selector = FindVisualChild<ComboBox>(container);
        if (selector is not null)
        {
            selector.SelectedItem = name;
            UpdateMissingSampleCue(selector, track);
        }
    }

    private void UpdateMissingSampleCue(ComboBox selector, Track track)
    {
        var container = ItemsControl.ContainerFromElement(TrackList, selector) as ListBoxItem;
        var cue = container is null ? null : FindVisualChild<TextBlock>(container, "MissingSampleText");
        if (cue is null) return;
        var selected = track.Samples.FirstOrDefault()?.FileName;
        cue.Text = selected is null ? "No sample selected. Use Load Sample."
            : viewModel.SampleFiles.Any(name => string.Equals(name, selected, StringComparison.OrdinalIgnoreCase))
                ? string.Empty : $"Missing sample: {selected}";
        cue.Visibility = cue.Text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
    }

    private static T? FindVisualChild<T>(DependencyObject root, string? name = null) where T : DependencyObject
    {
        if (root is T match && (name is null || root is FrameworkElement element && element.Name == name)) return match;
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var found = FindVisualChild<T>(VisualTreeHelper.GetChild(root, index), name);
            if (found is not null) return found;
        }
        return null;
    }

    public Task FlushSavesAsync() => viewModel.FlushSavesAsync();
    public bool HasPendingSaves => viewModel.HasPendingSaves;

    private void OnWaveformScrollBarScroll(object sender, ScrollEventArgs e)
    {
        if (!viewModel.CanPlay || deactivationStarted) return;
        WaveformScrollViewer.ScrollToHorizontalOffset(e.NewValue);
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

    private void OnTracksCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action != NotifyCollectionChangedAction.Remove || e.OldItems is null || deactivationStarted)
            return;
        var focus = Keyboard.FocusedElement as DependencyObject;
        for (var current = focus; current is not null; current = GetInputParent(current))
        {
            if (current is FrameworkElement element && e.OldItems.Contains(element.DataContext))
            {
                var index = Math.Clamp(e.OldStartingIndex, 0, Math.Max(0, viewModel.Tracks.Count - 1));
                Dispatcher.BeginInvoke(DispatcherPriority.Input, () =>
                {
                    if (deactivationStarted) return;
                    TrackList.UpdateLayout();
                    var container = TrackList.ItemContainerGenerator.ContainerFromIndex(index) as ListBoxItem;
                    var next = container is null ? null : FindVisualChild<BalloonButton>(container);
                    if (next?.Focus() != true) WaveformScrollViewer.Focus();
                });
                break;
            }
        }
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
        var offset = Math.Clamp(zoomAnchorFraction * extent - zoomAnchorViewportX, 0, maximum);
        hasPendingZoomAnchor = false;
        WaveformScrollViewer.ScrollToHorizontalOffset(offset);
    }

    private void OnWaveformMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        WaveformScrollViewer.Focus();
        if (!WaveformDisplay.SeekAt(e.GetPosition(WaveformDisplay).X)) return;
        e.Handled = true;
    }

    internal bool HandlePlaybackKey(Key key, ModifierKeys modifiers, bool isRepeat, DependencyObject? origin)
    {
        if (modifiers != ModifierKeys.None || deactivationStarted || IsEditingInput(origin)) return false;
        if (key == Key.Space)
        {
            if (!viewModel.PlayPauseCommand.CanExecute(null)) return false;
            if (!isRepeat) viewModel.PlayPauseCommand.Execute(null);
            return true;
        }
        if (key is not (Key.Left or Key.Right) || IsNativeArrowInput(origin)) return false;
        var command = key == Key.Left ? viewModel.PreviousGridLineCommand : viewModel.NextGridLineCommand;
        if (!command.CanExecute(null)) return false;
        command.Execute(null);
        RevealPlaybackPosition();
        return true;
    }

    private static bool IsNativeArrowInput(DependencyObject? origin)
    {
        var actionFocused = false;
        for (var current = origin; current is not null; current = GetInputParent(current))
        {
            if (current is ComboBox or RangeBase) return true;
            if (current is ButtonBase) actionFocused = true;
            if (current is Selector or ListBoxItem) return !actionFocused;
        }
        return false;
    }

    private void RevealPlaybackPosition()
    {
        var duration = viewModel.Waveform?.Duration.TotalSeconds ?? 0;
        var extent = WaveformDisplay.ActualWidth;
        var viewport = WaveformScrollViewer.ViewportWidth;
        if (duration <= 0 || !double.IsFinite(extent) || extent <= 0 ||
            !double.IsFinite(viewport) || viewport <= 0) return;
        var x = Math.Clamp(viewModel.Position.TotalSeconds / duration * extent, 0, extent);
        var offset = WaveformScrollViewer.HorizontalOffset;
        if (x < offset) WaveformScrollViewer.ScrollToHorizontalOffset(x);
        else if (x > offset + viewport)
            WaveformScrollViewer.ScrollToHorizontalOffset(Math.Min(WaveformScrollViewer.ScrollableWidth, x - viewport));
    }

    private static bool IsEditingInput(DependencyObject? origin)
    {
        for (var current = origin; current is not null; current = GetInputParent(current))
        {
            if (current is TextBoxBase or PasswordBox) return true;
            if (current is ComboBox) return true;
        }
        return false;
    }

    private static DependencyObject? GetInputParent(DependencyObject current) =>
        current is Visual or Visual3D
            ? VisualTreeHelper.GetParent(current)
            : LogicalTreeHelper.GetParent(current);

    internal bool HandleWaveformWheel(ModifierKeys modifiers, int delta)
    {
        if (modifiers is not ModifierKeys.Control and not ModifierKeys.Alt and not ModifierKeys.Shift ||
            !viewModel.CanZoom)
        {
            wheelMode = ModifierKeys.None;
            wheelRemainder = 0;
            return false;
        }

        if (wheelMode != modifiers)
        {
            wheelMode = modifiers;
            wheelRemainder = 0;
        }

        wheelRemainder += delta;
        var steps = wheelRemainder / Mouse.MouseWheelDeltaForOneLine;
        wheelRemainder %= Mouse.MouseWheelDeltaForOneLine;
        if (modifiers == ModifierKeys.Shift)
        {
            for (var index = 0; index < Math.Abs(steps); index++)
            {
                if (steps > 0) WaveformScrollViewer.LineLeft();
                else WaveformScrollViewer.LineRight();
            }
            return true;
        }

        var increase = steps > 0;
        for (var index = 0; index < Math.Abs(steps); index++)
        {
            var command = modifiers == ModifierKeys.Control
                ? increase ? viewModel.ZoomInCommand : viewModel.ZoomOutCommand
                : increase ? viewModel.IncreaseSubdivisionCommand : viewModel.DecreaseSubdivisionCommand;
            if (!command.CanExecute(null)) break;
            command.Execute(null);
        }
        return true;
    }

    private void OnWaveformKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Handled) return;
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
        if (e.Key is Key.Left or Key.Right)
        {
            e.Handled = HandlePlaybackKey(e.Key, ModifierKeys.None, e.IsRepeat,
                e.OriginalSource as DependencyObject);
            return;
        }
        switch (e.Key)
        {
            case Key.Home: WaveformScrollViewer.ScrollToLeftEnd(); break;
            case Key.End: WaveformScrollViewer.ScrollToRightEnd(); break;
            default: return;
        }
        e.Handled = true;
    }
}
