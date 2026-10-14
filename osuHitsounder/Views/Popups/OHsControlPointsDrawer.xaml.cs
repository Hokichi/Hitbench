using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using osuHitsounder.Resources.CustomControls;
using osuHitsounder.ViewModels.Popups;

namespace osuHitsounder.Views.Popups;

public partial class OHsControlPointsDrawer : UserControl, IOHsShellDrawer
{
    private readonly ControlPointsDrawerVM viewModel;
    public OHsControlPointsDrawer(ControlPointsDrawerVM viewModel)
    {
        InitializeComponent(); this.viewModel = viewModel; DataContext = viewModel;
        KeyboardNavigation.SetTabNavigation(this, KeyboardNavigationMode.Continue);
    }
    public event EventHandler? CloseRequested;
    public bool CanDismiss => viewModel.CanClose;
    public bool TryPrepareClose()
    {
        foreach (var input in Descendants(PointRows).OfType<NumericUpDown>())
            if (!input.CommitInput()) { input.Focus(); return false; }
        return viewModel.PrepareClose();
    }
    public void FocusInitialAction() => AddPointButton.Focus();
    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        yield return root;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            foreach (var child in Descendants(VisualTreeHelper.GetChild(root, i))) yield return child;
    }
    internal bool HandleNavigationKey(Key key, Key systemKey, ModifierKeys modifiers)
    {
        key = key == Key.System ? systemKey : key;
        var scroll = Descendants(PointRows).OfType<ScrollViewer>().FirstOrDefault();
        if (scroll is null) return false;
        if (modifiers == ModifierKeys.Alt && key is Key.Home or Key.End)
        {
            if (PointRows.Items.Count == 0) return false;
            var index = key == Key.End ? PointRows.Items.Count - 1 : 0;
            Descendants(PointRows).OfType<VirtualizingStackPanel>().FirstOrDefault()?.BringIndexIntoViewPublic(index);
            PointRows.UpdateLayout();
            if (PointRows.ItemContainerGenerator.ContainerFromIndex(index) is DependencyObject row)
                Descendants(row).OfType<NumericUpDown>().FirstOrDefault()?.MoveFocus(new TraversalRequest(FocusNavigationDirection.First));
            return true;
        }
        if (modifiers == ModifierKeys.None && key == Key.PageDown) scroll.PageDown();
        else if (modifiers == ModifierKeys.None && key == Key.PageUp) scroll.PageUp();
        else return false;
        return true;
    }
    private void OnCloseClick(object sender, RoutedEventArgs e) => RequestClose();
    private void RequestClose() { if (CanDismiss) CloseRequested?.Invoke(this, EventArgs.Empty); }
    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        if (Descendants(PointRows).OfType<ComboBox>().Any(c => c.IsDropDownOpen)) { base.OnPreviewKeyDown(e); return; }
        if (e.Key == Key.Escape) { e.Handled = true; RequestClose(); return; }
        if (SaveStatusHost.IsKeyboardFocusWithin) { base.OnPreviewKeyDown(e); return; }
        if (HandleNavigationKey(e.Key, e.SystemKey, Keyboard.Modifiers)) { e.Handled = true; return; }
        base.OnPreviewKeyDown(e);
    }
}
