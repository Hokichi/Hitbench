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
    private readonly CancellationTokenSource viewLifetime = new();
    private string? initialFolder;
    private Task? initialReveal;
    private bool initialFolderRevealed, isScrubbing;
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
        KeyboardNavigationPolicy.EnableTabNavigation(this);
        KeyboardNavigationPolicy.EnableTabNavigation((DependencyObject)Content);
        Loaded += OnPopupLoaded;
        DirectoryTree.Loaded += OnTreeLoaded;
        DirectoryTree.SizeChanged += OnTreeSizeChanged;
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
        initialFolder = viewModel.CurrentDirectory;
        if (DirectoryTree.IsLoaded) await RevealInitialFolderAsync();
    }

    public async Task DeactivateAsync()
    {
        if (deactivated) return;
        deactivated = true;
        initialFocus?.Abort();
        viewLifetime.Cancel();
        Loaded -= OnPopupLoaded;
        DirectoryTree.Loaded -= OnTreeLoaded;
        DirectoryTree.SizeChanged -= OnTreeSizeChanged;
        PlaybackTimeline.ValueChanged -= OnTimelineValueChanged;
        PlaybackTimeline.RemoveHandler(Thumb.DragStartedEvent, new DragStartedEventHandler(OnTimelineDragStarted));
        PlaybackTimeline.RemoveHandler(Thumb.DragCompletedEvent, new DragCompletedEventHandler(OnTimelineDragCompleted));
        DpiChanged -= OnPopupDpiChanged;
        if (sizingOwner is not null) sizingOwner.SizeChanged -= OnOwnerSizeChanged;
        await viewModel.DeactivateAsync();
        if (initialReveal is not null) await initialReveal;
        viewLifetime.Dispose();
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
        DispatcherPriority.Input, new Action(() => { if (!deactivated) DirectoryTextBox.Focus(); }));

    private async void OnTreeLoaded(object sender, RoutedEventArgs e) =>
        await HandleInteractionAsync(RevealInitialFolderAsync);

    private async void OnTreeSizeChanged(object sender, SizeChangedEventArgs e) =>
        await HandleInteractionAsync(RevealInitialFolderAsync);

    internal Task RevealInitialFolderAsync()
    {
        if (deactivated || initialFolderRevealed || string.IsNullOrEmpty(initialFolder) ||
            DirectoryTree.ActualWidth <= 0 || DirectoryTree.ActualHeight <= 0) return Task.CompletedTask;
        if (initialReveal is { IsCompleted: false }) return initialReveal;
        return initialReveal = RevealInitialFolderCoreAsync();
    }

    private async Task RevealInitialFolderCoreAsync()
    {
        try
        {
            ItemsControl owner = DirectoryTree;
            var entries = viewModel.Roots.AsEnumerable();
            while (!deactivated)
            {
                var entry = entries.FirstOrDefault(item => item.IsDirectory && (SameFolder(item.FullPath, initialFolder!) ||
                    initialFolder!.StartsWith(System.IO.Path.EndsInDirectorySeparator(item.FullPath)
                        ? item.FullPath : item.FullPath + System.IO.Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)));
                if (entry is null) return;
                owner.ApplyTemplate();
                owner.UpdateLayout();
                var index = owner.Items.IndexOf(entry);
                if (owner.ItemContainerGenerator.ContainerFromIndex(index) is not TreeViewItem)
                {
                    var panel = Descendants(owner).OfType<VirtualizingStackPanel>().FirstOrDefault(value => ItemsControl.GetItemsOwner(value) == owner);
                    panel?.BringIndexIntoViewPublic(index);
                }
                await Dispatcher.InvokeAsync(owner.UpdateLayout, DispatcherPriority.Loaded, viewLifetime.Token);
                if (owner.ItemContainerGenerator.ContainerFromIndex(index) is not TreeViewItem container) return;
                if (SameFolder(entry.FullPath, initialFolder!))
                {
                    var header = container.Template.FindName("Row", container) as FrameworkElement;
                    (header ?? container).BringIntoView();
                    await Dispatcher.InvokeAsync(DirectoryTree.UpdateLayout, DispatcherPriority.Loaded, viewLifetime.Token);
                    var target = header ?? container;
                    var bounds = target.TransformToAncestor(DirectoryTree).TransformBounds(new Rect(target.RenderSize));
                    initialFolderRevealed = bounds.Height > 0 && bounds.Top >= 0 && bounds.Bottom <= DirectoryTree.ActualHeight;
                    if (initialFolderRevealed) DirectoryTree.SizeChanged -= OnTreeSizeChanged;
                    return;
                }
                owner = container;
                entries = entry.Children;
            }
        }
        catch (OperationCanceledException) { }
    }

    private static bool SameFolder(string left, string right) => string.Equals(
        System.IO.Path.TrimEndingDirectorySeparator(left), System.IO.Path.TrimEndingDirectorySeparator(right), StringComparison.OrdinalIgnoreCase);

    private static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
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

    private async void OnFolderExpanded(object sender, RoutedEventArgs e)
    {
        if (e.OriginalSource != sender || sender is not TreeViewItem { DataContext: SampleBrowserEntry entry }) return;
        await HandleInteractionAsync(() => viewModel.ExpandAsync(entry));
    }

    private async void OnTreeSelectionChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (isSelectingFileRow) return;
        if (e.NewValue is SampleBrowserEntry entry)
        {
            if (ReferenceEquals(entry, viewModel.SelectedFile) || entry.IsDirectory &&
                string.Equals(entry.FullPath, viewModel.CurrentDirectory, StringComparison.OrdinalIgnoreCase)) return;
            await HandleInteractionAsync(() => viewModel.SelectAsync(entry));
        }
    }

    private async void OnFileRowMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.Handled || e.OriginalSource is not DependencyObject source ||
            !ReferenceEquals(sender, GetFileRow(source))) return;
        e.Handled = true;
        await HandleFileRowClickAsync(source, e.ClickCount);
    }

    internal Task HandleFileRowClickAsync(DependencyObject source, int clickCount)
    {
        if (deactivated || clickCount is not (1 or 2) || GetFileRow(source) is not { DataContext: SampleBrowserEntry entry } row)
            return Task.CompletedTask;
        isSelectingFileRow = true;
        try
        {
            row.Focus();
            row.SetCurrentValue(TreeViewItem.IsSelectedProperty, true);
        }
        finally { isSelectingFileRow = false; }
        return HandleInteractionAsync(() => clickCount == 2
            ? viewModel.SetCheckedAsync(entry, true) : viewModel.ActivateFileAsync(entry));
    }

    private TreeViewItem? GetFileRow(DependencyObject source)
    {
        for (DependencyObject? current = source; current is not null; current = DependencyObjectTree.GetParent(current))
        {
            if (current is ToggleButton) return null;
            if (current is TreeViewItem row)
                return row is { IsEnabled: true, DataContext: SampleBrowserEntry { IsDirectory: false, IsPlaceholder: false } } &&
                    DependencyObjectTree.IsDescendantOf(row, DirectoryTree) ? row : null;
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

    private async void OnDirectoryKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || Keyboard.Modifiers != ModifierKeys.None) return;
        e.Handled = true;
        await HandleInteractionAsync(() => viewModel.NavigateCommand.ExecuteAsync(null));
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (!e.Handled && e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.None &&
            e.OriginalSource is not TextBox && e.OriginalSource is not TreeViewItem && e.OriginalSource is not CheckBox &&
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
