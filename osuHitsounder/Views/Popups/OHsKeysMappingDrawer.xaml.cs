using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using osuHitsounder.Resources.CustomControls;
using osuHitsounder.Resources.DataModels;
using osuHitsounder.ViewModels.Popups;
namespace osuHitsounder.Views.Popups;

public partial class OHsKeysMappingDrawer : UserControl, IOHsShellDrawer
{
    private readonly KeysMappingDrawerVM viewModel;
    public OHsKeysMappingDrawer(KeysMappingDrawerVM viewModel)
    { InitializeComponent(); this.viewModel = viewModel; DataContext = viewModel; KeyboardNavigation.SetTabNavigation(this, KeyboardNavigationMode.Cycle); }
    public event EventHandler? CloseRequested;
    public bool CanDismiss => viewModel.CanClose;
    public bool TryPrepareClose() { if (!CanDismiss) return false; viewModel.PrepareClose(); return true; }
    public void FocusInitialAction()
    { KeyRows.UpdateLayout(); Descendants(KeyRows).OfType<TextBox>().FirstOrDefault(t => t.IsEnabled)?.Focus(); }
    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    { yield return root; for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++) foreach (var child in Descendants(VisualTreeHelper.GetChild(root, i))) yield return child; }
    internal bool HandleNavigationKey(Key key, ModifierKeys modifiers)
    {
        var scroll = Descendants(KeyRows).OfType<ScrollViewer>().FirstOrDefault(); if (scroll is null) return false;
        if (modifiers == ModifierKeys.Alt && key is Key.End or Key.Home)
        {
            if (KeyRows.Items.Count == 0) return false;
            var index = key == Key.End ? viewModel.Rows.Count - 1 : 0;
            Descendants(KeyRows).OfType<VirtualizingStackPanel>().FirstOrDefault()?.BringIndexIntoViewPublic(index);
            KeyRows.UpdateLayout();
            if (KeyRows.ItemContainerGenerator.ContainerFromIndex(index) is DependencyObject container)
                Descendants(container).OfType<TextBox>().FirstOrDefault(input => input.IsEnabled)?.Focus();
            return true;
        }
        if (modifiers == ModifierKeys.None && key == Key.PageDown) scroll.PageDown();
        else if (modifiers == ModifierKeys.None && key == Key.PageUp) scroll.PageUp();
        else return false;
        KeyRows.UpdateLayout(); FocusInitialAction(); return true;
    }
    internal bool HandleNavigationKey(Key key, Key systemKey, ModifierKeys modifiers) =>
        HandleNavigationKey(key == Key.System ? systemKey : key, modifiers);
    private void OnCloseClick(object sender, RoutedEventArgs e) { if (CanDismiss) CloseRequested?.Invoke(this, EventArgs.Empty); }
    private async void OnMappingLostFocus(object sender, KeyboardFocusChangedEventArgs e)
    { if (sender is TextBox { DataContext: KeyMappingEntry row } input) await CommitInputAsync(row.Key, input.Text); }
    private async void OnMappingKeyDown(object sender, KeyEventArgs e)
    { if (e.Key == Key.Enter && sender is TextBox { DataContext: KeyMappingEntry row } input) { e.Handled = true; await CommitInputAsync(row.Key, input.Text); } }
    private async Task CommitInputAsync(string key, string text)
    { try { await viewModel.CommitMappingAsync(key, text); } catch (OperationCanceledException) { } catch (Exception error) { viewModel.ErrorMessage = error.Message; } }
    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Escape) { e.Handled = true; if (CanDismiss) CloseRequested?.Invoke(this, EventArgs.Empty); return; }
        if (HandleNavigationKey(e.Key, e.SystemKey, Keyboard.Modifiers)) { e.Handled = true; return; }
        base.OnPreviewKeyDown(e);
    }
}
