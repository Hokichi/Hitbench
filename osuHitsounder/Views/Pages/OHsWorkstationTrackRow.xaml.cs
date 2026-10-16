using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.ComponentModel;
using osuHitsounder.ViewModels.Shell;
using osuHitsounder.Resources.CustomControls;
using osuHitsounder.Resources.DataModels;
using osuHitsounder.Core.Entities;
using Track = osuHitsounder.Core.Entities.Track;

namespace osuHitsounder.Views.Pages;

public partial class OHsWorkstationTrackRow : UserControl
{
    private WorkstationPageVM viewModel => ViewModel!;
    private WorkstationPageVM? subscribedViewModel;
    public OHsWorkstationTrackRow()
    {
        InitializeComponent();
        ((ObjectDataProvider)Resources["OHsWorkstationPageSource"]).ObjectInstance = this;
        Loaded += (_, _) => { Subscribe(); RefreshPresentation(); };
        Unloaded += (_, _) => Unsubscribe();
    }
    public static readonly DependencyProperty TrackProperty = DependencyProperty.Register(nameof(Track), typeof(Track), typeof(OHsWorkstationTrackRow));
    public Track? Track { get => (Track?)GetValue(TrackProperty); set => SetValue(TrackProperty, value); }
    public static readonly DependencyProperty ViewModelProperty = DependencyProperty.Register(nameof(ViewModel), typeof(WorkstationPageVM), typeof(OHsWorkstationTrackRow), new PropertyMetadata(null, (d, _) => ((OHsWorkstationTrackRow)d).Subscribe()));
    public WorkstationPageVM? ViewModel { get => (WorkstationPageVM?)GetValue(ViewModelProperty); set => SetValue(ViewModelProperty, value); }
    private void Subscribe()
    {
        if (ReferenceEquals(subscribedViewModel, ViewModel)) return;
        Unsubscribe();
        subscribedViewModel = ViewModel;
        if (subscribedViewModel is not null) subscribedViewModel.PropertyChanged += OnPresentationChanged;
    }
    private void Unsubscribe()
    {
        if (subscribedViewModel is not null) subscribedViewModel.PropertyChanged -= OnPresentationChanged;
        subscribedViewModel = null;
    }
    private void OnPresentationChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(WorkstationPageVM.SampleFiles) or nameof(WorkstationPageVM.SamplesRevision) or nameof(WorkstationPageVM.TrackAuditionRevision)) RefreshPresentation();
    }
    public BeatGridView? SampleTimeline => FindVisualChild<BeatGridView>(this);
    public void PrepareDrawing() => SampleTimeline?.PrepareDrawing();
    public void RefreshPresentation()
    {
        if (viewModel is null) return;
        RefreshSampleSelectors(this);
        RefreshTrackAuditionControls(this);
        RefreshTrackSamplePresentation();
    }
    public IInputElement? GetRemovalFocusTarget() => FindVisualChild<BalloonButton>(this);
    public bool HasExpandedControlFocus(DependencyObject? origin)
    {
        for (var current = origin; current is not null && !ReferenceEquals(current, this); current = VisualTreeHelper.GetParent(current))
            if (current is FrameworkElement { Name: "TrackLeftExpandedControls" or "TrackRightExpandedControls" }) return true;
        return false;
    }
    public static readonly DependencyProperty TimelineContentWidthProperty = DependencyProperty.Register(nameof(TimelineContentWidth), typeof(double), typeof(OHsWorkstationTrackRow), new PropertyMetadata(0d));
    public double TimelineContentWidth { get => (double)GetValue(TimelineContentWidthProperty); set => SetValue(TimelineContentWidthProperty, value); }
    public static readonly DependencyProperty TimelineViewportWidthProperty = DependencyProperty.Register(nameof(TimelineViewportWidth), typeof(double), typeof(OHsWorkstationTrackRow), new PropertyMetadata(0d));
    public double TimelineViewportWidth { get => (double)GetValue(TimelineViewportWidthProperty); set => SetValue(TimelineViewportWidthProperty, value); }
    public static readonly DependencyProperty TimelineHorizontalOffsetProperty = DependencyProperty.Register(nameof(TimelineHorizontalOffset), typeof(double), typeof(OHsWorkstationTrackRow), new PropertyMetadata(0d));
    public double TimelineHorizontalOffset { get => (double)GetValue(TimelineHorizontalOffsetProperty); set => SetValue(TimelineHorizontalOffsetProperty, value); }
    private static T? FindAncestor<T>(DependencyObject? value) where T : DependencyObject
    {
        for (var current = value; current is not null; current = VisualTreeHelper.GetParent(current)) if (current is T match) return match;
        return null;
    }
    private static T? FindVisualChild<T>(DependencyObject root, string? name = null) where T : DependencyObject
    {
        if (root is T match && (name is null || root is FrameworkElement e && e.Name == name)) return match;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            if (FindVisualChild<T>(VisualTreeHelper.GetChild(root, i), name) is { } found) return found;
        return null;
    }
    private void OnSampleSelectorLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is ComboBox { Name: "TrackSampleFileSelector", DataContext: Track track } selector)
        {
            selector.Tag = true;
            var saved = track.SelectedSampleFileName;
            selector.SelectedItem = viewModel.SampleFiles.FirstOrDefault(name =>
                string.Equals(name, saved, StringComparison.OrdinalIgnoreCase));
            selector.Tag = false;
            UpdateMissingSampleCue(selector, track);
        }
    }

    private void OnTrackControlDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (sender is ToggleButton toggle) { OnTrackAuditionLoaded(toggle, new RoutedEventArgs()); return; }
        if (sender is ComboBox { Name: "TrackSampleFileSelector" } selector) OnSampleSelectorLoaded(selector, new RoutedEventArgs());
        else if (sender is ComboBox metadata) OnTrackMetadataSelectorLoaded(metadata, new RoutedEventArgs());
        else if (sender is Slider slider) OnVolumeSliderLoaded(slider, new RoutedEventArgs());
        else if (sender is TextBox input) OnTrackNameLoaded(input, new RoutedEventArgs());
    }

    private void OnTrackNameLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is not TextBox { DataContext: Track track } input) return;
        input.Tag = true;
        input.SetCurrentValue(TextBox.TextProperty, track.Name);
        input.Tag = false;
    }

    private void OnTrackNameTextChanged(object sender, TextChangedEventArgs e)
    {
        if (sender is TextBox { IsLoaded: true, DataContext: Track track } input && input.Tag is not true)
            viewModel.SetTrackName(track, input.Text);
    }

    private void RefreshSampleSelectors(DependencyObject root)
    {
        if (root is ComboBox { Name: "TrackSampleFileSelector", DataContext: Track _ } selector)
        {
            OnSampleSelectorLoaded(selector, new RoutedEventArgs());
            return;
        }
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
            RefreshSampleSelectors(VisualTreeHelper.GetChild(root, index));
    }

    private void OnSampleSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is ComboBox selector && selector.IsLoaded && selector.Tag is not true && selector.DataContext is Track track)
        {
            if (selector.SelectedItem is string fileName &&
                !string.Equals(track.SelectedSampleFileName, fileName, StringComparison.OrdinalIgnoreCase))
                viewModel.SelectSample(track, fileName);
            UpdateMissingSampleCue(selector, track);
        }
    }

    private void OnTrackMetadataSelectorLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is not ComboBox { DataContext: Track track } selector) return;
        selector.Tag = true;
        selector.SetCurrentValue(ComboBox.SelectedItemProperty, selector.Name switch { "TrackSampleTypeSelector" => track.SampleType, _ => track.Addition });
        selector.Tag = false;
    }

    private void OnTrackMetadataSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is not ComboBox { IsLoaded: true, DataContext: Track track } selector || selector.Tag is true) return;
        if (selector.Name == "TrackSampleTypeSelector" && selector.SelectedItem is osuHitsounder.Core.Enums.SampleType type) { viewModel.SetTrackSampleType(track, type); return; }
        if (selector.SelectedItem is not string value) return;
        if (selector.Name == "TrackAdditionSelector") viewModel.SetTrackAddition(track, value);
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

    private void OnSamplePlacementRequested(object sender, SamplePlacementRequestedEventArgs e)
    {
        if (sender is BeatGridView { DataContext: Track track })
            viewModel.PlaceSample(track, e.LeftBoundaryMilliseconds);
        e.Handled = true;
    }

    private void OnSampleRemovalRequested(object sender, SampleRemovalRequestedEventArgs e)
    {
        if (sender is BeatGridView { DataContext: Track track })
            viewModel.RemoveSample(track, e.Sample);
        e.Handled = true;
    }

    private void UpdateMissingSampleCue(ComboBox selector, Track track)
    {
        RefreshTrackSamplePresentation();
    }

    private void OnTrackAuditionLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is not ToggleButton { DataContext: Track track } toggle) return;
        toggle.Tag = true;
        toggle.SetCurrentValue(ToggleButton.IsCheckedProperty, toggle.Name == "TrackMuteToggle"
            ? viewModel.IsTrackMuted(track) : viewModel.IsTrackSoloed(track));
        toggle.Tag = false;
    }

    private void OnTrackAuditionChanged(object sender, RoutedEventArgs e)
    {
        if (sender is not ToggleButton { IsLoaded: true, DataContext: Track track } toggle || toggle.Tag is true) return;
        if (toggle.Name == "TrackMuteToggle") viewModel.SetTrackMuted(track, toggle.IsChecked is true);
        else if (toggle.Name == "TrackSoloToggle") viewModel.SetTrackSoloed(track, toggle.IsChecked is true);
        OnTrackAuditionLoaded(toggle, new RoutedEventArgs());
    }

    private void RefreshTrackAuditionControls(DependencyObject root)
    {
        if (root is ToggleButton { Name: "TrackMuteToggle" or "TrackSoloToggle" } toggle)
            OnTrackAuditionLoaded(toggle, new RoutedEventArgs());
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            RefreshTrackAuditionControls(VisualTreeHelper.GetChild(root, i));
    }

    private void OnTrackControlStackLoaded(object sender, RoutedEventArgs e) => UpdateTrackHeightMinimum(sender);
    private void OnTrackControlStackSizeChanged(object sender, SizeChangedEventArgs e) => UpdateTrackHeightMinimum(sender);

    private void UpdateTrackHeightMinimum(object sender)
    {
        if (viewModel is null || sender is not StackPanel stack) return;
        var row=FindAncestor<Grid>(stack);
        if(row is null)return;
        var left = FindVisualChild<StackPanel>(row, "TrackLeftControls");
        var right = FindVisualChild<StackPanel>(row, "TrackRightControls");
        if (left is not null && (right is not null || stack.DataContext is osuHitsounder.Core.Entities.InstrumentalTrack))
            viewModel.SetTrackHeightMinimum(Math.Max(left.DesiredSize.Height, right?.DesiredSize.Height??0));
    }

    private void OnTrackGridHostLoaded(object sender, RoutedEventArgs e) => RefreshTrackSamplePresentation();
    private void OnTrackGridHostDataContextChanged(object sender, DependencyPropertyChangedEventArgs e) => RefreshTrackSamplePresentation();
    private void RefreshTrackSamplePresentation() => RefreshTrackSamplePresentation(this);
    private void RefreshTrackSamplePresentation(DependencyObject root)
    {
        if (root is Grid { Name: "TrackGridHost", DataContext: Track track } host)
        {
            var state = viewModel.GetSampleAvailability(track); host.Tag = state;
            var content = FindVisualChild<Grid>(host, "TrackGridContent");
            var status = FindVisualChild<TextBlock>(host, "MissingGridStatus");
            if (content is not null) content.IsEnabled = state == TrackSampleAvailability.Available;
            if (status is not null) status.Text = state switch
            {
                TrackSampleAvailability.Missing => "Sample not found.",
                TrackSampleAvailability.Unselected => "No sample selected. Choose a sample or use Manage Sample.",
                _ => string.Empty
            };
            return;
        }
        for (var i=0;i<VisualTreeHelper.GetChildrenCount(root);i++) RefreshTrackSamplePresentation(VisualTreeHelper.GetChild(root,i));
    }    private void OnSampleSelectionRequested(object sender, SampleSelectionRequestedEventArgs e)
    {
        if (sender is BeatGridView { DataContext: Track track }) viewModel.SelectNode(track, e.Sample, false);
        e.Handled = true;
    }
}

