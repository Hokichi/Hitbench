using System.Windows;
using System.Windows.Input;
using Hitbench.Resources.CustomControls;
using Hitbench.Services.Ui;
using Hitbench.ViewModels.Popups;
using Hitbench.Resources.DataModels;

namespace Hitbench.Views.Popups;

public partial class OHsMidiImportPopup : BasePopup
{
    private readonly OHsMidiImportPopupVM viewModel;
    private readonly Action<bool> complete;
    private bool completed;
    protected override bool ShouldBubbleApplicationClose => false;

    public OHsMidiImportPopup(OHsMidiImportPopupVM vm, Action<bool>? complete = null)
    {
        viewModel = vm;
        this.complete = complete ?? (accepted => DialogResult = accepted);
        InitializeComponent(); DataContext = vm;
        Loaded += (_, _) => { RefreshWorkArea(); FocusMode(); };
        DpiChanged += (_, _) => RefreshWorkArea();
    }

    private void RefreshWorkArea()
    {
        var area = WindowWorkArea.GetClientSize(this);
        if (area.Width > 0) MaxWidth = area.Width;
        if (area.Height > 0) MaxHeight = area.Height;
    }
    private void FocusMode() => TimingModes.Items.OfType<SegmentedToggleOption>().FirstOrDefault(o => Equals(o.Value, viewModel.SelectedMode))?.Focus();
    internal void TryComplete(bool accepted)
    {
        if (completed || accepted && !viewModel.CanImport) return;
        completed = true; complete(accepted);
    }
    internal bool HandleKey(Key key, DependencyObject? origin = null)
    {
        if (key == Key.Escape) { TryComplete(false); return true; }
        if (key == Key.Enter) { TryComplete(!ReferenceEquals(origin, CloseAction)); return true; }
        if (key is Key.Up or Key.Down or Key.Left or Key.Right or Key.Home or Key.End)
        {
            var forward = key == Key.Down || key == Key.Right && FlowDirection == FlowDirection.LeftToRight || key == Key.Left && FlowDirection == FlowDirection.RightToLeft;
            viewModel.SelectedMode = key == Key.Home ? OHsMidiImportMode.AsIs : key == Key.End ? OHsMidiImportMode.ProjectBpm :
                (OHsMidiImportMode)(((int)viewModel.SelectedMode + (forward ? 1 : 2)) % 3);
            FocusMode(); return true;
        }
        return false;
    }
    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        if (HandleKey(e.Key == Key.System ? e.SystemKey : e.Key, Keyboard.FocusedElement as DependencyObject)) { e.Handled = true; return; }
        base.OnPreviewKeyDown(e);
    }
    private void OnImport(object sender, RoutedEventArgs e) => TryComplete(true);
    private void OnCancel(object sender, RoutedEventArgs e) => TryComplete(false);
}
