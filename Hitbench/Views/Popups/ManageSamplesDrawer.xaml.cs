using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Data;
using System.ComponentModel;
using Hitbench.Resources.CustomControls;
using Hitbench.Resources.DataModels;
using Hitbench.ViewModels.Popups;

namespace Hitbench.Views.Popups;

public partial class ManageSamplesDrawer : UserControl,IOHsShellDrawer
{
    private readonly ManageSamplesPopupVM viewModel;
    private BalloonButton? editAction;
    private SampleManagementEntry? editingRow;

    public ManageSamplesDrawer(ManageSamplesPopupVM viewModel)
    {
        InitializeComponent(); this.viewModel = viewModel; DataContext = viewModel;
        InitializeSampleDrag();
        ((ObjectDataProvider)Resources["OHsManageSamplesOwner"]).ObjectInstance = this;
        Loaded += OnDrawerLoaded;
        Unloaded += OnDrawerUnloaded;
        KeyboardNavigation.SetTabNavigation(this, KeyboardNavigationMode.Cycle);
    }

    public event EventHandler? CloseRequested;
    public bool CanDismiss => viewModel.CanClose;
    public bool TryPrepareClose() => CanDismiss;

    private void OnSampleSectionChanged(object sender, RoutedEventArgs e)
    {
        if (SkinSectionRow is null || ProjectSectionRow is null || SkinDefaultsExpander is null || ProjectSamplesExpander is null) return;
        SkinSectionRow.Height = SkinDefaultsExpander.IsExpanded ? new GridLength(1, GridUnitType.Star) : GridLength.Auto;
        ProjectSectionRow.Height = ProjectSamplesExpander.IsExpanded ? new GridLength(1, GridUnitType.Star) : GridLength.Auto;
    }
    private void OnCloseClick(object sender, RoutedEventArgs e)
    {
        if (viewModel.CanClose) CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    private void OnSharedWaveformRequested(object? sender, EventArgs e) { if (sender is FrameworkElement element) OnWaveformRowLoaded(element, new RoutedEventArgs()); }
    private async void OnWaveformRowLoaded(object sender, RoutedEventArgs e)
    {
        try { if (sender is FrameworkElement { DataContext: SampleManagementEntry row }) await viewModel.LoadWaveformAsync(row); }
        catch (Exception error) { System.Diagnostics.Trace.TraceWarning("Sample waveform presentation failed: {0}", error.Message); }
    }
    private void OnWaveformRowChanged(object sender, DependencyPropertyChangedEventArgs e)
    { if (sender is FrameworkElement { IsLoaded: true }) OnWaveformRowLoaded(sender, new RoutedEventArgs()); }
    private void OnEditActionClick(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem item && ItemsControl.ItemsControlFromItemContainer(item) is ContextMenu menu)
            editAction = menu.PlacementTarget as BalloonButton;
    }

    private void OnDrawerLoaded(object sender, RoutedEventArgs e)
    {
        viewModel.PropertyChanged -= OnEditingChanged;
        viewModel.PropertyChanged += OnEditingChanged;
        editingRow = viewModel.ActiveEdit;
    }
    private void OnDrawerUnloaded(object sender, RoutedEventArgs e) => viewModel.PropertyChanged -= OnEditingChanged;
    private void OnEditingChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(ManageSamplesPopupVM.ActiveEdit)) return;
        var previous = editingRow;
        editingRow = viewModel.ActiveEdit;
        if (editingRow is null && previous is not null)
            Dispatcher.BeginInvoke(new Action(() => RestoreRowAction(previous)));
    }
    private void RestoreRowAction(SampleManagementEntry row)
    {
        if (!IsLoaded || !viewModel.CanClose) return;
        var action = editAction is { IsEnabled: true } && ReferenceEquals(editAction.DataContext, row)
            ? editAction : Descendants(SampleRows).OfType<BalloonComboBox>().FirstOrDefault(button => ReferenceEquals(button.DataContext, row));
        action?.Focus();
    }

    private void OnEditorVisibilityChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (sender is TextBox { IsVisible: true } input)
            Dispatcher.BeginInvoke(new Action(() => { if (input.IsVisible && input.IsEnabled) { input.Focus(); input.SelectAll(); } }));
    }

    internal bool HandleEscape()
    {
        if (viewModel.IsBusy) return true;
        if (viewModel.ActiveEdit is { } row)
        {
            viewModel.CancelEditCommand.Execute(null);
            RestoreRowAction(row);
        }
        else if (viewModel.CanClose) CloseRequested?.Invoke(this, EventArgs.Empty);
        return true;
    }

    internal Task HandleEnterAsync() => viewModel.CommitRenameCommand.CanExecute(null)
        ? viewModel.CommitRenameCommand.ExecuteAsync(null) : Task.CompletedTask;

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Escape) { e.Handled = HandleEscape(); return; }
        base.OnPreviewKeyDown(e);
    }

    private async void OnEditorKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        e.Handled = true;
        try { await HandleEnterAsync(); }
        catch (Exception error) { System.Diagnostics.Trace.TraceError("Sample edit failed: {0}", error); }
    }

    public void FocusInitialAction()
    {
        SampleRows.UpdateLayout();
        var action = Descendants(SampleRows).OfType<BalloonButton>().FirstOrDefault(b => b.IsEnabled);
        if (action is not null) action.Focus(); else AddNewSampleButton.Focus();
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        yield return root;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            foreach (var child in Descendants(VisualTreeHelper.GetChild(root, i))) yield return child;
    }
}
