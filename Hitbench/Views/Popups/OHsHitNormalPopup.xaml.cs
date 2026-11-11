using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using Hitbench.Resources.CustomControls;
using Hitbench.Resources.DataModels;
using Hitbench.Resources.Helpers;
using Hitbench.Services.Ui;
using Hitbench.ViewModels.Popups;

namespace Hitbench.Views.Popups;

public partial class OHsHitNormalPopup : BasePopup
{
    private readonly OHsHitNormalPopupVM viewModel;
    private readonly Action<bool>? complete;
    private readonly CollectionViewSource catalog = new();
    private bool updatingCatalog;
    private bool completed;
    private bool released;

    public OHsHitNormalPopup(OHsHitNormalPopupVM viewModel) : this(viewModel, null) { }
    internal OHsHitNormalPopup(OHsHitNormalPopupVM viewModel, Action<bool>? complete)
    {
        this.viewModel = viewModel;
        this.complete = complete;
        InitializeComponent();
        DataContext = viewModel;
        SizeToContent = SizeToContent.Manual;
        Width = 960; Height = 640;
        var grouping = new PropertyGroupDescription(nameof(OHsHitNormalSample.Group));
        foreach (var group in viewModel.Groups) grouping.GroupNames.Add(group);
        catalog.GroupDescriptions.Add(grouping);
        catalog.Source = viewModel.CatalogSamples;
        SampleList.ItemsSource = catalog.View;
        viewModel.PropertyChanging += OnCatalogChanging;
        viewModel.PropertyChanged += OnCatalogChanged;
        RestoreCurrentSelection();
        Loaded += OnPopupLoaded;
        Closed += OnPopupClosed;
        DpiChanged += (_, _) => RefreshWorkArea();
        LocationChanged += (_, _) => { if (IsLoaded) RefreshWorkArea(); };
    }

    protected override bool ShouldBubbleApplicationClose => false;
    private void OnPopupLoaded(object sender, RoutedEventArgs e) { RefreshWorkArea(); FocusSlot(viewModel.SelectedSlot); }
    private void OnPopupClosed(object? sender, EventArgs e) => ReleaseOwner();

    private void OnCatalogChanging(object? sender, PropertyChangingEventArgs e)
    {
        if (e.PropertyName is not (nameof(OHsHitNormalPopupVM.NormalPath) or nameof(OHsHitNormalPopupVM.SoftPath) or
            nameof(OHsHitNormalPopupVM.DrumPath) or nameof(OHsHitNormalPopupVM.SelectedSlot))) return;
        updatingCatalog = true;
    }

    private void OnCatalogChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(OHsHitNormalPopupVM.PinnedSamples)) return;
        updatingCatalog = true;
        try
        {
            catalog.View.Refresh();
            SampleList.SelectedItem = viewModel.SelectedSlot.Sample;
        }
        finally { updatingCatalog = false; }
    }

    private void RestoreCurrentSelection()
    {
        updatingCatalog = true;
        try { SampleList.SelectedItem = viewModel.SelectedSlot.Sample; }
        finally { updatingCatalog = false; }
    }

    private void OnSampleSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (updatingCatalog || released || !ReferenceEquals(e.OriginalSource, SampleList)) return;
        if (e.AddedItems.OfType<OHsHitNormalSample>().FirstOrDefault() is { } sample &&
            viewModel.AssignSampleCommand.CanExecute(sample)) viewModel.AssignSampleCommand.Execute(sample);
    }

    private async void OnWaveformRequested(object? sender, EventArgs e)
    {
        if (released || sender is not OHsSampleRow row) return;
        var sample = row.DataContext switch { OHsHitNormalSample entry => entry, OHsHitNormalSlot slot => slot.Sample, _ => null };
        if (sample is null) return;
        try { await viewModel.LoadWaveformAsync(sample); }
        catch (Exception error) { System.Diagnostics.Trace.TraceWarning("Hitnormal waveform request failed: {0}", error.Message); }
    }

    public void ReleaseOwner()
    {
        if (released) return;
        released = true;
        viewModel.PropertyChanging -= OnCatalogChanging;
        viewModel.PropertyChanged -= OnCatalogChanged;
        SampleList.ItemsSource = null;
        catalog.Source = null;
    }

    private void RefreshWorkArea() => ApplyWorkArea(WindowWorkArea.GetClientSize(Owner ?? this),
        Math.Max(0, ActualHeight - ContentGrid.ActualHeight));

    internal void ApplyWorkArea(Size area, double chrome = 0)
    {
        if (area.IsEmpty || !double.IsFinite(area.Width) || !double.IsFinite(area.Height) || area.Width <= 0 || area.Height <= 0) return;
        MaxWidth = area.Width; Width = Math.Min(960, area.Width);
        MaxHeight = area.Height; Height = Math.Min(640, area.Height);
        ContentGrid.MaxHeight = Math.Max(0, Height - (double.IsFinite(chrome) ? Math.Max(0, chrome) : 0));
    }

    internal bool TryComplete(bool accepted)
    {
        if (completed || accepted && !viewModel.CanSave) return false;
        completed = true;
        if (complete is not null) complete(accepted); else if (IsVisible) DialogResult = accepted;
        return true;
    }

    internal bool HandleShortcut(Key key, ModifierKeys modifiers, DependencyObject? originalSource = null)
    {
        if (key == Key.F6 && modifiers is ModifierKeys.None or ModifierKeys.Shift)
        {
            NextFocusTarget(originalSource, modifiers == ModifierKeys.Shift).Focus();
            return true;
        }
        if (modifiers != ModifierKeys.Alt) return false;
        switch (key)
        {
            case Key.N: FocusSlot(viewModel.Slots[0]); break;
            case Key.S: FocusSlot(viewModel.Slots[1]); break;
            case Key.D: FocusSlot(viewModel.Slots[2]); break;
            case Key.L: FocusSlot(viewModel.SelectedSlot); break;
            case Key.R: SampleList.Focus(); SampleList.ScrollIntoView(viewModel.SelectedSlot.Sample); break;
            case Key.D1: case Key.D2: case Key.D3:
                var group = viewModel.Groups[(int)key - (int)Key.D1 + 1];
                group.IsExpanded = !group.IsExpanded;
                break;
            case Key.V: TryComplete(true); break;
            case Key.C: TryComplete(false); break;
            default: return false;
        }
        return true;
    }

    internal FrameworkElement NextFocusTarget(DependencyObject? source, bool reverse)
    {
        FrameworkElement[] targets = [SlotList, SampleList, CancelAction, SaveAction];
        var index = Array.FindIndex(targets, t => DependencyObjectTree.IsDescendantOf(source, t));
        if (index < 0) return reverse ? targets[^1] : targets[0];
        return targets[(index + (reverse ? targets.Length - 1 : 1)) % targets.Length];
    }

    private void FocusSlot(OHsHitNormalSlot slot)
    {
        SlotList.SelectedItem = slot;
        SlotList.ScrollIntoView(slot);
        SlotList.UpdateLayout();
        if (SlotList.ItemContainerGenerator.ContainerFromItem(slot) is ListBoxItem item) item.Focus();
        else SlotList.Focus();
    }

    internal bool HandleKey(Key key, DependencyObject? originalSource = null)
    {
        if (key == Key.Escape) { TryComplete(false); return true; }
        if (DependencyObjectTree.IsDescendantOf(originalSource, SampleList) && key is Key.Up or Key.Down or Key.Home or Key.End)
        {
            NavigateSamples(key);
            return true;
        }
        if (key != Key.Enter) return false;
        var toggle = DependencyObjectTree.FindAncestor<ToggleButton>(originalSource);
        var expander = DependencyObjectTree.FindAncestor<Expander>(toggle);
        if (expander is not null) { expander.IsExpanded = !expander.IsExpanded; return true; }
        if (DependencyObjectTree.IsDescendantOf(originalSource, SampleList))
        {
            if (SampleList.SelectedItem is OHsHitNormalSample sample && viewModel.AssignSampleCommand.CanExecute(sample))
                viewModel.AssignSampleCommand.Execute(sample);
            return true;
        }
        TryComplete(!DependencyObjectTree.IsDescendantOf(originalSource, CancelAction));
        return true;
    }

    private int SourceGroupIndex(OHsHitNormalSample sample) => sample.Source switch
    {
        OHsHitNormalSampleSource.Skin => 1, OHsHitNormalSampleSource.Mapset => 2, _ => 3
    };

    private void NavigateSamples(Key key)
    {
        // Use source order so moving the current row into the pinned group cannot trap arrow navigation.
        var entries = viewModel.CatalogSamples.Where(s => s.Group.IsPinned || viewModel.Groups[SourceGroupIndex(s)].IsExpanded)
            .OrderBy(s => s.IsDefault ? 0 : SourceGroupIndex(s))
            .ThenBy(s => s.DisplayName, StringComparer.OrdinalIgnoreCase).ThenBy(s => s.DisplayName, StringComparer.Ordinal).ToArray();
        if (entries.Length == 0) return;
        var current = Array.IndexOf(entries, SampleList.SelectedItem);
        var index = key switch
        {
            Key.Home => 0, Key.End => entries.Length - 1,
            Key.Up => Math.Max(0, current - 1), _ => Math.Min(entries.Length - 1, current + 1)
        };
        SampleList.SelectedItem = entries[index];
        SampleList.ScrollIntoView(entries[index]);
        SampleList.Focus();
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (HandleShortcut(key, Keyboard.Modifiers, e.OriginalSource as DependencyObject) ||
            Keyboard.Modifiers == ModifierKeys.None && HandleKey(key, e.OriginalSource as DependencyObject)) e.Handled = true;
        else base.OnPreviewKeyDown(e);
    }

    protected override void OnCloseButtonClick() => TryComplete(false);
    private void OnCancel(object sender, RoutedEventArgs e) => TryComplete(false);
    private void OnSave(object sender, RoutedEventArgs e) => TryComplete(true);
}
