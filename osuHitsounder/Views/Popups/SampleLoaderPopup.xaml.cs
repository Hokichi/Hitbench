using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using osuHitsounder.Resources.CustomControls;
using osuHitsounder.Resources.DataModels;
using osuHitsounder.Resources.Helpers;
using osuHitsounder.Services.Ui;
using osuHitsounder.ViewModels.Popups;

namespace osuHitsounder.Views.Popups;

public partial class SampleLoaderPopup : BasePopup
{
    private readonly SampleLoaderPopupVM viewModel;
    private readonly Action<bool> completeDialog;
    private readonly Func<Window, Size> getWorkArea;
    private Window? sizingOwner;
    private bool deactivated;
    private DispatcherOperation? initialFocus;
    private bool navigationHadFocus;
    private int lastAnnouncedCount = -1;
    private bool isScrubbing;
    private bool isSelectingFileRow;
    private SampleBrowserEntry? scrubbingFile;
    public IReadOnlyList<string> Result { get; private set; } = Array.Empty<string>();

    public SampleLoaderPopup(SampleLoaderPopupVM viewModel) : this(viewModel, null, null)
    {
    }

    internal SampleLoaderPopup(SampleLoaderPopupVM viewModel, Action<bool>? completeDialog,
        Func<Window, Size>? getWorkArea = null)
    {
        InitializeComponent();
        this.viewModel = viewModel;
        this.completeDialog = completeDialog ?? (accepted => DialogResult = accepted);
        this.getWorkArea = getWorkArea ?? WindowWorkArea.GetClientSize;
        DataContext = viewModel;
        InitializePalette();
        KeyboardNavigation.SetTabNavigation(this, KeyboardNavigationMode.None);
        viewModel.PropertyChanged += OnBrowserStateChanged;
        Loaded += OnPopupLoaded;
    }

    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        if (GetTemplateChild("PART_CloseButton") is UIElement close)
        {
            KeyboardNavigationPolicy.SetAllowTabNavigation(close, false);
            KeyboardNavigation.SetIsTabStop(close, false);
        }
    }

    protected override double RequestedOverlayOpacity => 0.58;
    protected override bool RequestedOverlayBlur => false;

    protected override bool ShouldBubbleApplicationClose => false;

    public async Task InitializeAsync(SamplePickerContext context, CancellationToken token)
    {
        sizingOwner = Owner;
        if (sizingOwner is not null) sizingOwner.SizeChanged += OnOwnerSizeChanged;
        DpiChanged += OnPopupDpiChanged;
        UpdateSize();
        await viewModel.InitializeAsync(context, token);
        RefreshBreadcrumbPresentation();
    }

    public Task InitializeAsync(CancellationToken token) => InitializeAsync((string?)null, token);

    public async Task InitializeAsync(string? initialDirectory, CancellationToken token)
    {
        sizingOwner = Owner;
        if (sizingOwner is not null) sizingOwner.SizeChanged += OnOwnerSizeChanged;
        DpiChanged += OnPopupDpiChanged;
        UpdateSize();
        await viewModel.InitializeAsync(initialDirectory, token);
        RefreshBreadcrumbPresentation();
    }

    public async Task DeactivateAsync()
    {
        if (deactivated) return;
        deactivated = true;
        SystemParameters.StaticPropertyChanged -= OnSystemPaletteChanged;
        initialFocus?.Abort();
        Loaded -= OnPopupLoaded;
        viewModel.PropertyChanged -= OnBrowserStateChanged;
        DpiChanged -= OnPopupDpiChanged;
        if (sizingOwner is not null) sizingOwner.SizeChanged -= OnOwnerSizeChanged;
        await viewModel.DeactivateAsync();
    }

    private static readonly DependencyPropertyKey IsCompactLayoutPropertyKey = DependencyProperty.RegisterReadOnly(
        nameof(IsCompactLayout), typeof(bool), typeof(SampleLoaderPopup), new PropertyMetadata(false));

    public static readonly DependencyProperty IsCompactLayoutProperty = IsCompactLayoutPropertyKey.DependencyProperty;
    public bool IsCompactLayout => (bool)GetValue(IsCompactLayoutProperty);

    private async void OnLocationSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (LocationList.SelectedItem is SampleBrowserLocation location && !ReferenceEquals(location, viewModel.ActiveLocation))
        {
            await HandleInteractionAsync(() => viewModel.NavigateLocationCommand.ExecuteAsync(location));
            if (!deactivated) LocationList.SetCurrentValue(Selector.SelectedItemProperty, viewModel.ActiveLocation);
        }
    }

    private bool activatingBasket;

    private async void OnBasketSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (activatingBasket || e.AddedItems.OfType<SampleBrowserEntry>().FirstOrDefault() is not { } entry || ReferenceEquals(entry, viewModel.SelectedFile)) return;
        activatingBasket = true;
        try { await HandleInteractionAsync(() => viewModel.ActivateSelectedCommand.ExecuteAsync(entry)); }
        finally { activatingBasket = false; }
    }

    private async void OnBasketKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || SelectedSamplesList.SelectedItem is not SampleBrowserEntry entry) return;
        e.Handled = true;
        await HandleInteractionAsync(() => viewModel.ActivateSelectedCommand.ExecuteAsync(entry));
    }

    private void OnRemoveSelectedClick(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: SampleBrowserEntry entry }) return;
        var index = SelectedSamplesList.Items.IndexOf(entry);
        Dispatcher.BeginInvoke(DispatcherPriority.Input, () =>
        {
            if (deactivated) return;
            if (SelectedSamplesList.Items.Count > 0 && SelectedSamplesList.ItemContainerGenerator.ContainerFromIndex(Math.Clamp(index, 0, SelectedSamplesList.Items.Count - 1)) is ListBoxItem next) next.Focus();
            else SelectedSamplesList.Focus();
        });
    }

    private void OnLocationMenuOpened(object sender, RoutedEventArgs e)
    {
        if (sender is not ContextMenu menu) return;
        foreach (var location in menu.Items.OfType<SampleBrowserLocation>())
            if (menu.ItemContainerGenerator.ContainerFromItem(location) is MenuItem item)
                item.SetCurrentValue(MenuItem.IsCheckedProperty, ReferenceEquals(location, viewModel.ActiveLocation));
    }

    private void UpdateSize()
    {
        Width = 1200;
        Height = 640;
        SetValue(IsCompactLayoutPropertyKey, false);
    }

    private void OnOwnerSizeChanged(object sender, SizeChangedEventArgs e) => UpdateSize();

    private void OnPopupDpiChanged(object sender, DpiChangedEventArgs e) => UpdateSize();

    private void OnPopupLoaded(object sender, RoutedEventArgs e) => initialFocus = Dispatcher.BeginInvoke(
        DispatcherPriority.Input, new Action(() => { if (!deactivated) DirectoryContents.Focus(); }));

    private void OnWaveformSeekMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (!viewModel.CanSeek || e.OriginalSource is not DependencyObject source) return;
        for (DependencyObject? current = source; current is not null && !ReferenceEquals(current, sender); current = DependencyObjectTree.GetParent(current))
            if (current is Thumb) return;
        if (PreviewWaveform.SeekAt(e.GetPosition(PreviewWaveform).X)) e.Handled = true;
    }

    private async void OnTimelineValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (isScrubbing || !viewModel.CanSeek || Math.Abs(e.NewValue - viewModel.PlaybackPositionSeconds) < .000001) return;
        await HandleInteractionAsync(() => viewModel.SeekAsync(e.NewValue));
    }

    private void OnTimelineDragStarted(object sender, DragStartedEventArgs e)
    {
        if (!viewModel.CanSeek) return;
        isScrubbing = true;
        scrubbingFile = viewModel.SelectedFile;
    }

    private void OnBrowserStateChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SampleLoaderPopupVM.SelectionAnnouncement) && lastAnnouncedCount != viewModel.CheckedCount)
        {
            lastAnnouncedCount = viewModel.CheckedCount;
            System.Windows.Automation.Peers.UIElementAutomationPeer.FromElement(SelectedCount)?.RaiseAutomationEvent(System.Windows.Automation.Peers.AutomationEvents.LiveRegionChanged);
        }
        if (e.PropertyName != nameof(SampleLoaderPopupVM.IsNavigating)) return;
        if (!viewModel.IsNavigating) RefreshBreadcrumbPresentation();
        if (viewModel.IsNavigating)
            navigationHadFocus |= DirectoryContents.IsKeyboardFocusWithin || DirectoryBreadcrumbs.IsKeyboardFocusWithin;
        else if (navigationHadFocus)
        {
            navigationHadFocus = false;
            initialFocus = Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() => { if (!deactivated) DirectoryContents.Focus(); }));
        }
    }

    private void OnBreadcrumbWidthChanged(object sender, SizeChangedEventArgs e) => RefreshBreadcrumbPresentation();

    internal static int GetFirstVisibleBreadcrumb(IReadOnlyList<double> actionWidths, double availableWidth, double overflowWidth, double separatorWidth)
    {
        if (actionWidths.Count == 0 || actionWidths.Sum() + (actionWidths.Count - 1) * separatorWidth <= availableWidth) return 0;
        var start = actionWidths.Count;
        var used = overflowWidth + separatorWidth;
        while (start > 0)
        {
            var candidate = actionWidths[start - 1] + (start < actionWidths.Count ? separatorWidth : 0);
            if (start < actionWidths.Count && used + candidate > availableWidth) break;
            used += candidate;
            start--;
        }
        return start;
    }

    private void RefreshBreadcrumbPresentation()
    {
        if (deactivated) return;
        var all = viewModel.Breadcrumbs.ToArray();
        var width = (EarlierFolders.Parent as FrameworkElement)?.ActualWidth ?? 0;
        if (width <= 0) width = 400;
        var dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var widths = all.Select(item => new FormattedText(item.Name, System.Globalization.CultureInfo.CurrentUICulture, FlowDirection,
            new Typeface((FontFamily)FindResource(item.IsCurrent ? "Bold" : "Regular"), FontStyles.Normal,
                item.IsCurrent ? FontWeights.Bold : FontWeights.Normal, FontStretches.Normal), 12, Brushes.White, dpi).Width + 24).ToArray();
        var start = GetFirstVisibleBreadcrumb(widths, width, 36, 16);
        DirectoryBreadcrumbs.ItemsSource = all.Skip(start).ToArray();
        EarlierFolders.Visibility = start > 0 ? Visibility.Visible : Visibility.Collapsed;
        var menu = EarlierFolders.ContextMenu;
        menu.Items.Clear();
        foreach (var item in all.Take(start))
        {
            var action = new MenuItem
            {
                Header = item.Name,
                Command = viewModel.NavigateCommand,
                CommandParameter = item.FullPath,
                Style = (Style)FindResource("OHsActionMenuItemStyle"),
                IsTabStop = false
            };
            KeyboardNavigationPolicy.SetAllowTabNavigation(action, false);
            System.Windows.Automation.AutomationProperties.SetName(action, item.Name);
            menu.Items.Add(action);
        }
    }

    private async void OnContentsSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (isSelectingFileRow) return;
        if (DirectoryContents.SelectedItem is SampleBrowserEntry entry)
            await HandleInteractionAsync(() => viewModel.SelectAsync(entry));
    }

    private async void OnContentsKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Space && Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) && DirectoryContents.SelectedItem is SampleBrowserEntry rangeEnd)
        {
            e.Handled = true;
            await HandleInteractionAsync(() => viewModel.SetRangeCheckedAsync(rangeEnd, true));
            return;
        }
        if (e.Key == Key.Space && Keyboard.Modifiers == ModifierKeys.None && DirectoryContents.SelectedItem is SampleBrowserEntry file && !file.IsDirectory)
        {
            e.Handled = true;
            viewModel.SetRangeAnchor(file);
            await HandleInteractionAsync(() => viewModel.SetCheckedAsync(file, !file.IsChecked));
            return;
        }
        if (e.Key != Key.Enter || Keyboard.Modifiers != ModifierKeys.None) return;
        e.Handled = true;
        await HandleInteractionAsync(ActivateSelectedEntryAsync);
    }

    internal Task ActivateSelectedEntryAsync() => DirectoryContents.SelectedItem is SampleBrowserEntry entry
        ? entry.IsDirectory ? viewModel.NavigateCommand.ExecuteAsync(entry.FullPath) : viewModel.SetCheckedAsync(entry, true)
        : Task.CompletedTask;

    private async void OnEntryRowMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.Handled || e.OriginalSource is not DependencyObject source ||
            !ReferenceEquals(sender, GetEntryRow(source))) return;
        e.Handled = true;
        await HandleEntryRowClickAsync(source, e.ClickCount, Keyboard.Modifiers.HasFlag(ModifierKeys.Shift));
    }

    internal Task HandleEntryRowClickAsync(DependencyObject source, int clickCount, bool extendRange = false)
    {
        if (deactivated || clickCount is not (1 or 2) || GetEntryRow(source) is not { DataContext: SampleBrowserEntry entry } row)
            return Task.CompletedTask;
        isSelectingFileRow = true;
        try
        {
            row.Focus();
            row.SetCurrentValue(ListBoxItem.IsSelectedProperty, true);
        }
        finally { isSelectingFileRow = false; }
        if (entry.IsDirectory) return HandleInteractionAsync(() => clickCount == 2 ? viewModel.NavigateCommand.ExecuteAsync(entry.FullPath) : viewModel.SelectAsync(entry));
        if (extendRange) return HandleInteractionAsync(() => viewModel.SetRangeCheckedAsync(entry, true));
        viewModel.SetRangeAnchor(entry);
        return HandleInteractionAsync(() => clickCount == 2
            ? viewModel.SetCheckedAsync(entry, true) : viewModel.ActivateFileAsync(entry));
    }

    private ListBoxItem? GetEntryRow(DependencyObject source)
    {
        for (DependencyObject? current = source; current is not null; current = DependencyObjectTree.GetParent(current))
        {
            if (current is ToggleButton) return null;
            if (current is ListBoxItem row)
                return row is { IsEnabled: true, DataContext: SampleBrowserEntry } &&
                    DependencyObjectTree.IsDescendantOf(row, DirectoryContents) ? row : null;
        }
        return null;
    }

    private async void OnFileCheckClick(object sender, RoutedEventArgs e)
    {
        if (sender is not CheckBox { DataContext: SampleBrowserEntry entry } checkbox) return;
        e.Handled = true;
        var value = checkbox.IsChecked == true;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) await HandleInteractionAsync(() => viewModel.SetRangeCheckedAsync(entry, value));
        else { viewModel.SetRangeAnchor(entry); await HandleInteractionAsync(() => viewModel.SetCheckedAsync(entry, value)); }
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Tab) { e.Handled = true; return; }
        base.OnPreviewKeyDown(e);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (!e.Handled && e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.None &&
            e.OriginalSource is not BalloonButton && e.OriginalSource is not TextBox && e.OriginalSource is not ListBoxItem && e.OriginalSource is not CheckBox &&
            e.OriginalSource is not Slider && e.OriginalSource is not Thumb &&
            e.OriginalSource is not System.Windows.Controls.Primitives.ToggleButton)
        {
            e.Handled = true;
            OnImportClick(ImportButton, e);
        }
    }

    private async void OnImportClick(object sender, RoutedEventArgs e)
    {
        if (!viewModel.CanImport) return;
        await HandleInteractionAsync(async () =>
        {
            var paths = await viewModel.ConfirmAsync();
            if (paths.Count == 0 || deactivated) return;
            Result = paths;
            completeDialog(true);
        });
    }

    protected override void OnCloseButtonClick()
    {
        Result = Array.Empty<string>();
        completeDialog(false);
    }

    private async Task HandleInteractionAsync(Func<Task> action)
    {
        try { if (!deactivated) await action(); }
        catch (OperationCanceledException) { }
        catch (Exception error) { if (!deactivated) viewModel.DirectoryError = error.Message; }
    }
}