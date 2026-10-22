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
    private readonly Action<BalloonComboBox> presentMenu;
    public OHsWorkstationTrackRow() : this(null) { }
    internal OHsWorkstationTrackRow(Action<BalloonComboBox>? presentMenu)
    {
        this.presentMenu = presentMenu ?? (trigger => trigger.InvokeActionMenu());
        InitializeComponent();
        RefreshTrackPalette();
        PreviewMouseRightButtonDown += (_, e) => { if (IsTrackMenuOrigin(e.OriginalSource as DependencyObject)) e.Handled = true; };
        PreviewMouseRightButtonUp += (_, e) => { if (TryOpenTrackMenu(e.OriginalSource as DependencyObject)) e.Handled = true; };
        PreviewKeyDown += (_, e) => { if ((e.Key == Key.Apps || e.Key == Key.F10 && Keyboard.Modifiers == ModifierKeys.Shift) && TryOpenTrackMenu(e.OriginalSource as DependencyObject)) e.Handled = true; };
        ((ObjectDataProvider)Resources["OHsWorkstationTrackRowSource"]).ObjectInstance = this;
        Loaded += (_, _) => { UseTouchTargets = Tablet.TabletDevices.Cast<TabletDevice>().Any(device => device.Type == TabletDeviceType.Touch); Subscribe(); RefreshPresentation(); if (FindVisualChild<StackPanel>(this, "TrackLeftControls") is { } controls) UpdateTrackHeightMinimum(controls); };
        Unloaded += (_, _) => { if (Track is {} track) ViewModel?.CancelTrackRename(track); DetachActionMenu(); Unsubscribe(); };
    }
    public static readonly DependencyProperty UseTouchTargetsProperty = DependencyProperty.Register(nameof(UseTouchTargets), typeof(bool), typeof(OHsWorkstationTrackRow), new PropertyMetadata(false));
    public bool UseTouchTargets { get => (bool)GetValue(UseTouchTargetsProperty); set => SetValue(UseTouchTargetsProperty, value); }
    private void OpenTrackActionMenu(BalloonComboBox trigger) => presentMenu(trigger);
    private bool IsTrackMenuOrigin(DependencyObject? origin)
    {
        for (var current = origin; current is not null && current != this; current = current is Visual ? VisualTreeHelper.GetParent(current) : LogicalTreeHelper.GetParent(current))
            if (current is BeatGridView or ComboBox || current is TextBox { IsReadOnly: false }) return false;
        return FindVisualChild<BalloonComboBox>(this) is { IsMenuOwnerActive: true };
    }
    private bool TryOpenTrackMenu(DependencyObject? origin)
    {
        if (!IsTrackMenuOrigin(origin) || FindVisualChild<BalloonComboBox>(this) is not { } trigger) return false;
        OpenTrackActionMenu(trigger);
        return true;
    }
    public static readonly DependencyProperty TrackProperty = DependencyProperty.Register(nameof(Track), typeof(Track), typeof(OHsWorkstationTrackRow), new PropertyMetadata(null, OnTrackChanged));
    public Track? Track { get => (Track?)GetValue(TrackProperty); set => SetValue(TrackProperty, value); }
    public static readonly DependencyProperty ViewModelProperty = DependencyProperty.Register(nameof(ViewModel), typeof(WorkstationPageVM), typeof(OHsWorkstationTrackRow), new PropertyMetadata(null, OnOwnerChanged));
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
        if (e.PropertyName is nameof(WorkstationPageVM.SampleFiles) or nameof(WorkstationPageVM.SamplesRevision)
            or nameof(WorkstationPageVM.TrackAuditionRevision) or nameof(WorkstationPageVM.TrackPresentationRevision)
            or nameof(WorkstationPageVM.RenamingTrack) or nameof(WorkstationPageVM.TrackNameDraft)
            or nameof(WorkstationPageVM.IsTrackRenamePending)) RefreshPresentation();
    }
    public static readonly DependencyProperty TrackColorIndexProperty = DependencyProperty.Register(
        nameof(TrackColorIndex), typeof(int), typeof(OHsWorkstationTrackRow), new PropertyMetadata(0, (d, _) => ((OHsWorkstationTrackRow)d).RefreshTrackPalette()));
    public int TrackColorIndex { get => (int)GetValue(TrackColorIndexProperty); set => SetValue(TrackColorIndexProperty, value); }
    public static readonly DependencyProperty TrackBrushProperty = DependencyProperty.Register(
        nameof(TrackBrush), typeof(Brush), typeof(OHsWorkstationTrackRow));
    public Brush? TrackBrush => (Brush?)GetValue(TrackBrushProperty);
    private void RefreshTrackPalette()
    {
        if (Track is not InstrumentalTrack && Track?.Color is {} color)
            SetValue(TrackBrushProperty,osuHitsounder.Resources.Helpers.OHsTrackPalette.ResolveBrush(this,TrackColorIndex,color));
        else SetResourceReference(TrackBrushProperty,osuHitsounder.Resources.Helpers.OHsTrackPalette.GetResourceKey(TrackColorIndex));
    }

    public BeatGridView? SampleTimeline => FindVisualChild<BeatGridView>(this);
    public void PrepareDrawing() => SampleTimeline?.PrepareDrawing();
    public void RefreshPresentation()
    {
        if (viewModel is null) return;
        if (Track is {} current)
        {
            SetValue(IsInstrumentalTrackPropertyKey, current is InstrumentalTrack);
            SetCurrentValue(TrackColorIndexProperty, viewModel.Tracks.IndexOf(current));
            RefreshTrackPalette();
            if (!IsTrackCollapsed && current.IsCollapsed && HasExpandedControlFocus(Keyboard.FocusedElement as DependencyObject ?? FocusManager.GetFocusedElement(FocusManager.GetFocusScope(this)) as DependencyObject)) FindVisualChild<BalloonComboBox>(this)?.Focus();
            IsTrackCollapsed = current.IsCollapsed; CurrentTrackExtent = viewModel.GetTrackExtent(current);
            IsTrackMuted = current.IsTrackMuted; IsTrackSoloed = current.IsTrackSoloed;
            var wasRenaming = IsRenaming;
            IsRenaming = ReferenceEquals(viewModel.RenamingTrack, current);
            if (IsRenaming && !wasRenaming)
            {
                var owner = ViewModel; var generation = owner!.LoadGeneration;
                Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Input, () =>
                {
                    if (!IsLoaded || !ReferenceEquals(Track, current) || !ReferenceEquals(ViewModel, owner) || owner.LoadGeneration != generation || !ReferenceEquals(owner.RenamingTrack, current)) return;
                    var input = FindVisualChild<TextBox>(this); input?.Focus(); input?.SelectAll();
                });
            }
            SetCurrentValue(TrackNameDraftProperty, IsRenaming ? viewModel.TrackNameDraft : current.Name);
        }
        RefreshSampleSelectors(this);
        RefreshTrackAuditionControls(this);
        RefreshTrackSamplePresentation();
    }
    public IInputElement? GetRemovalFocusTarget() => FindVisualChild<BalloonComboBox>(this);
    public bool HasExpandedControlFocus(DependencyObject? origin)
    {
        var expanded = false;
        for (var current = origin; current is not null; current = current is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(current) : LogicalTreeHelper.GetParent(current))
        {
            if (ReferenceEquals(current, this)) return expanded;
            if (current is FrameworkElement { Name: "TrackLeftExpandedControls" or "TrackRightExpandedControls" }) expanded = true;
        }
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
        input.SetCurrentValue(TextBox.TextProperty, ReferenceEquals(viewModel.RenamingTrack, track) ? viewModel.TrackNameDraft : track.Name);
        input.Tag = false;
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
        if (left is null || right is null && stack.DataContext is not InstrumentalTrack) return;
        // Measure controls independently of the timeline's current extent, including touch target growth.
        left.Measure(new Size(left.ActualWidth, double.PositiveInfinity));
        right?.Measure(new Size(right.ActualWidth, double.PositiveInfinity));
        if (Track is { } track) viewModel.SetTrackMinimumExtent(track, track.IsCollapsed,
            Math.Max(left.DesiredSize.Height, right?.DesiredSize.Height ?? 0));
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
            if (content is not null) content.IsEnabled = state == TrackSampleAvailability.Available;
            return;
        }
        for (var i=0;i<VisualTreeHelper.GetChildrenCount(root);i++) RefreshTrackSamplePresentation(VisualTreeHelper.GetChild(root,i));
    }    private void OnSampleSelectionRequested(object sender, SampleSelectionRequestedEventArgs e)
    {
        if (sender is BeatGridView { DataContext: Track track }) viewModel.SelectNode(track, e.Sample, false);
        e.Handled = true;
    }    private static readonly DependencyPropertyKey IsTrackCollapsedPropertyKey = DependencyProperty.RegisterReadOnly(nameof(IsTrackCollapsed), typeof(bool), typeof(OHsWorkstationTrackRow), new PropertyMetadata(false));
    public static readonly DependencyProperty IsTrackCollapsedProperty = IsTrackCollapsedPropertyKey.DependencyProperty;
    public bool IsTrackCollapsed { get => (bool)GetValue(IsTrackCollapsedProperty); private set => SetValue(IsTrackCollapsedPropertyKey, value); }
    private static readonly DependencyPropertyKey IsTrackMutedPropertyKey = DependencyProperty.RegisterReadOnly(nameof(IsTrackMuted), typeof(bool), typeof(OHsWorkstationTrackRow), new PropertyMetadata(false));
    public static readonly DependencyProperty IsTrackMutedProperty = IsTrackMutedPropertyKey.DependencyProperty;
    public bool IsTrackMuted { get => (bool)GetValue(IsTrackMutedProperty); private set => SetValue(IsTrackMutedPropertyKey, value); }
    private static readonly DependencyPropertyKey IsTrackSoloedPropertyKey = DependencyProperty.RegisterReadOnly(nameof(IsTrackSoloed), typeof(bool), typeof(OHsWorkstationTrackRow), new PropertyMetadata(false));
    public static readonly DependencyProperty IsTrackSoloedProperty = IsTrackSoloedPropertyKey.DependencyProperty;
    public bool IsTrackSoloed { get => (bool)GetValue(IsTrackSoloedProperty); private set => SetValue(IsTrackSoloedPropertyKey, value); }
    private static readonly DependencyPropertyKey IsRenamingPropertyKey = DependencyProperty.RegisterReadOnly(nameof(IsRenaming), typeof(bool), typeof(OHsWorkstationTrackRow), new PropertyMetadata(false));
    public static readonly DependencyProperty IsRenamingProperty = IsRenamingPropertyKey.DependencyProperty;
    public bool IsRenaming { get => (bool)GetValue(IsRenamingProperty); private set => SetValue(IsRenamingPropertyKey, value); }
    private static readonly DependencyPropertyKey CurrentTrackExtentPropertyKey = DependencyProperty.RegisterReadOnly(nameof(CurrentTrackExtent), typeof(double), typeof(OHsWorkstationTrackRow), new PropertyMetadata(36d));
    public static readonly DependencyProperty CurrentTrackExtentProperty = CurrentTrackExtentPropertyKey.DependencyProperty;
    public double CurrentTrackExtent { get => (double)GetValue(CurrentTrackExtentProperty); private set => SetValue(CurrentTrackExtentPropertyKey, value); }
    public static readonly DependencyProperty TrackNameDraftProperty = DependencyProperty.Register(nameof(TrackNameDraft), typeof(string), typeof(OHsWorkstationTrackRow), new PropertyMetadata("", (d,e) => { var row = (OHsWorkstationTrackRow)d; if (row.IsRenaming && ReferenceEquals(row.ViewModel?.RenamingTrack, row.Track)) row.ViewModel!.TrackNameDraft = (string)e.NewValue; }));
    public string TrackNameDraft { get => (string)GetValue(TrackNameDraftProperty); set => SetValue(TrackNameDraftProperty, value); }
    private static void OnTrackChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var row = (OHsWorkstationTrackRow)d;
        if (e.OldValue is Track old) row.ViewModel?.CancelTrackRename(old);
        row.DetachActionMenu();
        row.RefreshPresentation();
        row.AttachActionMenu();
    }
    private async void OnTrackNameLostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is not TextBox { DataContext: Track track } || !ReferenceEquals(track, Track)) return;
        try { await viewModel.CommitTrackRenameAsync(track); }
        catch (Exception error) { viewModel.ErrorMessage = error.Message; }
    }
    private async void OnTrackNameKeyDown(object sender, KeyEventArgs e)
    {
        if (sender is not TextBox { DataContext: Track track } || !ReferenceEquals(track, Track)) return;
        if (e.Key == Key.Escape) { e.Handled = true; viewModel.CancelTrackRename(track); }
        else if (e.Key == Key.Enter) { e.Handled = true; try { await viewModel.CommitTrackRenameAsync(track); } catch (Exception error) { viewModel.ErrorMessage = error.Message; } }
    }
    private static void OnOwnerChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var row = (OHsWorkstationTrackRow)d;
        if (e.OldValue is WorkstationPageVM old && row.Track is {} track) old.CancelTrackRename(track);
        row.DetachActionMenu(); row.Subscribe(); row.RefreshPresentation(); row.AttachActionMenu();
    }
    private void OnActionMenuLoaded(object sender, RoutedEventArgs e) => AttachActionMenu();
    private void DetachActionMenu()
    {
        if (FindVisualChild<BalloonComboBox>(this) is {} trigger) trigger.ContextMenu = null;
    }
    private void AttachActionMenu()
    {
        if (Track is null || ViewModel is null || FindVisualChild<BalloonComboBox>(this) is not {} trigger || trigger.ContextMenu is not null) return;
        var menu = (ContextMenu)FindResource("OHsWorkstationTrackActionMenu");
        menu.DataContext = Track;
        foreach (var item in menu.Items.OfType<MenuItem>())
        {
            item.DataContext = Track; item.CommandParameter = Track;
            BindingOperations.GetBindingExpression(item, MenuItem.CommandProperty)?.UpdateTarget();
        }
        trigger.ContextMenu = menu;
        if (Track is not InstrumentalTrack && menu.Items[2] is MenuItem appearance)
        {
            BindingOperations.ClearBinding(appearance, MenuItem.CommandProperty);
            appearance.Command = ViewModel.ChangeTrackNameAndColorCommand;
            appearance.Header = "Change Name and Color";
            appearance.Click += (_, _) => trigger.Focus();
        }
    }

    private static readonly DependencyPropertyKey IsInstrumentalTrackPropertyKey = DependencyProperty.RegisterReadOnly(nameof(IsInstrumentalTrack), typeof(bool), typeof(OHsWorkstationTrackRow), new PropertyMetadata(false));
    public static readonly DependencyProperty IsInstrumentalTrackProperty = IsInstrumentalTrackPropertyKey.DependencyProperty;
    public bool IsInstrumentalTrack => (bool)GetValue(IsInstrumentalTrackProperty);

}
