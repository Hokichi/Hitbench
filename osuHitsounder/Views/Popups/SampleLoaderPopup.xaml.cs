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
    private bool isScrubbing;
    private bool isSelectingFileRow;
    private SampleBrowserEntry? scrubbingFile;
    public IReadOnlyList<string> Result { get; private set; } = Array.Empty<string>();

    public SampleLoaderPopup(SampleLoaderPopupVM viewModel) : this(viewModel, null, null) { }

    internal SampleLoaderPopup(SampleLoaderPopupVM viewModel, Action<bool>? completeDialog,
        Func<Window, Size>? getWorkArea = null)
    {
        InitializeComponent();
        this.viewModel = viewModel;
        this.completeDialog = completeDialog ?? (accepted => DialogResult = accepted);
        this.getWorkArea = getWorkArea ?? WindowWorkArea.GetClientSize;
        DataContext = viewModel;
        viewModel.PropertyChanged += OnBrowserStateChanged;
        Loaded += OnPopupLoaded;
        PlaybackTimeline.ValueChanged += OnTimelineValueChanged;
        PlaybackTimeline.AddHandler(Thumb.DragStartedEvent, new DragStartedEventHandler(OnTimelineDragStarted));
        PlaybackTimeline.AddHandler(Thumb.DragCompletedEvent, new DragCompletedEventHandler(OnTimelineDragCompleted));
    }

    protected override bool ShouldBubbleApplicationClose => false;

    public Task InitializeAsync(CancellationToken token) => InitializeAsync(null, token);

    public async Task InitializeAsync(string? initialDirectory, CancellationToken token)
    {
        sizingOwner = Owner;
        if (sizingOwner is not null) sizingOwner.SizeChanged += OnOwnerSizeChanged;
        DpiChanged += OnPopupDpiChanged;
        UpdateSize();
        await viewModel.InitializeAsync(initialDirectory, token);
    }

    public async Task DeactivateAsync()
    {
        if (deactivated) return;
        deactivated = true;
        initialFocus?.Abort();
        Loaded -= OnPopupLoaded;
        viewModel.PropertyChanged -= OnBrowserStateChanged;
        PlaybackTimeline.ValueChanged -= OnTimelineValueChanged;
        PlaybackTimeline.RemoveHandler(Thumb.DragStartedEvent, new DragStartedEventHandler(OnTimelineDragStarted));
        PlaybackTimeline.RemoveHandler(Thumb.DragCompletedEvent, new DragCompletedEventHandler(OnTimelineDragCompleted));
        DpiChanged -= OnPopupDpiChanged;
        if (sizingOwner is not null) sizingOwner.SizeChanged -= OnOwnerSizeChanged;
        await viewModel.DeactivateAsync();
    }

    private void UpdateSize()
    {
        var area = getWorkArea(sizingOwner ?? this);
        if (area.IsEmpty || area.Width <= 0 || area.Height <= 0)
            area = sizingOwner?.RenderSize ?? SystemParameters.WorkArea.Size;
        var ownerSize = sizingOwner?.RenderSize ?? area;
        Width = Math.Max(1, Math.Min(960, Math.Min(area.Width, ownerSize.Width) - 32));
        Height = Math.Max(1, Math.Min(640, Math.Min(area.Height, ownerSize.Height) - 32));
    }

    private void OnOwnerSizeChanged(object sender, SizeChangedEventArgs e) => UpdateSize();
    private void OnPopupDpiChanged(object sender, DpiChangedEventArgs e) => UpdateSize();
    private void OnPopupLoaded(object sender, RoutedEventArgs e) => initialFocus = Dispatcher.BeginInvoke(
        DispatcherPriority.Input, new Action(() => { if (!deactivated) DirectoryContents.Focus(); }));

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
        var value = PlaybackTimeline.Value;
        BindingOperations.ClearBinding(PlaybackTimeline, RangeBase.ValueProperty);
        PlaybackTimeline.Value = value;
    }

    private async void OnTimelineDragCompleted(object sender, DragCompletedEventArgs e)
    {
        if (!isScrubbing) return;
        try
        {
            if (!e.Canceled && ReferenceEquals(scrubbingFile, viewModel.SelectedFile))
                await HandleInteractionAsync(() => viewModel.SeekAsync(PlaybackTimeline.Value));
        }
        finally
        {
            PlaybackTimeline.SetBinding(RangeBase.ValueProperty, new Binding(nameof(SampleLoaderPopupVM.PlaybackPositionSeconds)) { Mode = BindingMode.OneWay });
            isScrubbing = false;
            scrubbingFile = null;
        }
    }

    private void OnBrowserStateChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(SampleLoaderPopupVM.IsNavigating)) return;
        if (viewModel.IsNavigating)
            navigationHadFocus |= DirectoryContents.IsKeyboardFocusWithin || DirectoryBreadcrumbs.IsKeyboardFocusWithin;
        else if (navigationHadFocus)
        {
            navigationHadFocus = false;
            initialFocus = Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() => { if (!deactivated) DirectoryContents.Focus(); }));
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
        await HandleEntryRowClickAsync(source, e.ClickCount);
    }

    internal Task HandleEntryRowClickAsync(DependencyObject source, int clickCount)
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
        await HandleInteractionAsync(() => viewModel.SetCheckedAsync(entry, value));
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
