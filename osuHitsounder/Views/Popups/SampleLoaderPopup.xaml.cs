using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
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
    }

    protected override bool ShouldBubbleApplicationClose => false;

    public Task InitializeAsync(CancellationToken token) => InitializeAsync(null, token);

    public Task InitializeAsync(string? initialDirectory, CancellationToken token)
    {
        sizingOwner = Owner;
        if (sizingOwner is not null) sizingOwner.SizeChanged += OnOwnerSizeChanged;
        DpiChanged += OnPopupDpiChanged;
        UpdateSize();
        return viewModel.InitializeAsync(initialDirectory, token);
    }

    public async Task DeactivateAsync()
    {
        if (deactivated) return;
        deactivated = true;
        initialFocus?.Abort();
        Loaded -= OnPopupLoaded;
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
        DispatcherPriority.Input, new Action(() => { if (!deactivated) DirectoryTextBox.Focus(); }));

    private async void OnFolderExpanded(object sender, RoutedEventArgs e)
    {
        if (e.OriginalSource != sender || sender is not TreeViewItem { DataContext: SampleBrowserEntry entry }) return;
        await HandleInteractionAsync(() => viewModel.ExpandAsync(entry));
    }

    private async void OnTreeSelectionChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (e.NewValue is SampleBrowserEntry entry)
        {
            if (ReferenceEquals(entry, viewModel.SelectedFile) || entry.IsDirectory &&
                string.Equals(entry.FullPath, viewModel.CurrentDirectory, StringComparison.OrdinalIgnoreCase)) return;
            await HandleInteractionAsync(() => viewModel.SelectAsync(entry));
        }
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
