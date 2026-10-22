using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Soundie.Resources.CustomControls;
using Soundie.Resources.DataModels;
using Soundie.ViewModels.Popups;
namespace Soundie.Views.Popups;

public partial class OHsKeysMappingDrawer : UserControl, IOHsShellDrawer
{
    private readonly KeysMappingDrawerVM viewModel;
    public OHsKeysMappingDrawer(KeysMappingDrawerVM viewModel)
    {
        InitializeComponent(); this.viewModel = viewModel; DataContext = viewModel;
        KeyboardNavigation.SetTabNavigation(this, KeyboardNavigationMode.Cycle);
        KeySelector.PreviewMouseWheel += OnKeyboardWheel;
        Loaded += (_, _) => { if (!centered) KeySelector.LayoutUpdated += OnInitialKeyboardLayout; };
        Unloaded += (_, _) => KeySelector.LayoutUpdated -= OnInitialKeyboardLayout;
    }
    private void OnPianoScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (!ReferenceEquals(e.OriginalSource, sender) || sender is not ScrollViewer scroll || PianoScrollBar is null) return;
        PianoScrollBar.SetCurrentValue(System.Windows.Controls.Primitives.RangeBase.MaximumProperty, scroll.ScrollableWidth);
        PianoScrollBar.SetCurrentValue(System.Windows.Controls.Primitives.ScrollBar.ViewportSizeProperty, scroll.ViewportWidth);
        PianoScrollBar.SetCurrentValue(System.Windows.Controls.Primitives.RangeBase.ValueProperty, scroll.HorizontalOffset);
    }
    private void OnPianoScroll(object sender, System.Windows.Controls.Primitives.ScrollEventArgs e)
    {
        Descendants(KeySelector).OfType<ScrollViewer>().FirstOrDefault()?.ScrollToHorizontalOffset(e.NewValue);
        e.Handled = true;
    }
    private bool centered, reconciling;
    private int keyboardWheelRemainder;
    internal static double GetInitialKeyboardOffset(double start, double end, double viewport, double extent) =>
        !double.IsFinite(start + end + viewport + extent) || viewport <= 0 || extent <= 0 ? 0 : Math.Clamp((start + end - viewport) / 2,0,Math.Max(0,extent-viewport));
    private async void OnKeySelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (reconciling || e.AddedItems.OfType<KeyMappingEntry>().FirstOrDefault() is not { } row) return;
        try { await viewModel.SelectKeyAsync(row.Key); }
        catch (Exception error) { viewModel.ErrorMessage = error.Message; }
        finally { reconciling = true; KeySelector.SelectedItem = viewModel.SelectedEntry; reconciling = false; }
    }
    public event EventHandler? CloseRequested;
    public bool CanDismiss => viewModel.CanClose;
    public bool TryPrepareClose() { if (!CanDismiss) return false; viewModel.PrepareClose(); return true; }
    public void FocusInitialAction()
    {
        KeySelector.UpdateLayout();
        TryCenterKeyboard();
        LoadAction.Focus();
    }
    private void OnInitialKeyboardLayout(object? sender, EventArgs e) => TryCenterKeyboard();
    private void TryCenterKeyboard()
    {
        if (centered || !viewModel.CanEditMapping) return;
        if (Descendants(KeySelector).OfType<ScrollViewer>().FirstOrDefault() is not { ViewportWidth: > 0, ExtentWidth: > 0 } scroll ||
            KeySelector.ItemContainerGenerator.ContainerFromIndex(0) is not FrameworkElement { ActualWidth: > 0 } first) return;
        centered = true;
        KeySelector.LayoutUpdated -= OnInitialKeyboardLayout;
        // Four preceding octaves contain 28 natural keys; sharps overlay their boundaries.
        scroll.ScrollToHorizontalOffset(GetInitialKeyboardOffset(28 * first.ActualWidth, 35 * first.ActualWidth, scroll.ViewportWidth, scroll.ExtentWidth));
    }
    private void OnKeyboardWheel(object sender, MouseWheelEventArgs e)
    {
        if (Descendants(KeySelector).OfType<ScrollViewer>().FirstOrDefault() is not { } scroll) return;
        keyboardWheelRemainder += e.Delta;
        var steps = keyboardWheelRemainder / Mouse.MouseWheelDeltaForOneLine;
        keyboardWheelRemainder %= Mouse.MouseWheelDeltaForOneLine;
        // Use the platform's horizontal line scrolling, preserving high-resolution wheel deltas.
        for (var step = 0; step < Math.Abs(steps); step++)
            if (steps > 0) scroll.LineLeft(); else scroll.LineRight();
        e.Handled = true;
    }
    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    { yield return root; for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++) foreach (var child in Descendants(VisualTreeHelper.GetChild(root, i))) yield return child; }
    internal bool HandleNavigationKey(Key key, ModifierKeys modifiers)
    {
        var scroll = Descendants(KeySelector).OfType<ScrollViewer>().FirstOrDefault(); if (scroll is null) return false;
        if (modifiers == ModifierKeys.Alt && key is Key.End or Key.Home)
        {
            if (KeySelector.Items.Count == 0) return false;
            var index = key == Key.End ? viewModel.Rows.Count - 1 : 0;
            Descendants(KeySelector).OfType<OHsKeysMappingPianoPanel>().FirstOrDefault()?.BringIndexIntoViewPublic(index);
            KeySelector.UpdateLayout();
            if (KeySelector.ItemContainerGenerator.ContainerFromIndex(index) is DependencyObject container)
                (container as UIElement)?.Focus();
            return true;
        }
        if (modifiers == ModifierKeys.None && key == Key.PageDown) scroll.PageRight();
        else if (modifiers == ModifierKeys.None && key == Key.PageUp) scroll.PageLeft();
        else return false;
        KeySelector.UpdateLayout(); return true;
    }
    internal bool HandleNavigationKey(Key key, Key systemKey, ModifierKeys modifiers) =>
        HandleNavigationKey(key == Key.System ? systemKey : key, modifiers);
    private void OnCloseClick(object sender, RoutedEventArgs e) { if (CanDismiss) CloseRequested?.Invoke(this, EventArgs.Empty); }
    private async void OnMappingLostFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        for (var target = e.NewFocus as DependencyObject; target is not null; target = target is FrameworkContentElement content ? content.Parent : target is Visual ? VisualTreeHelper.GetParent(target) : null)
            if (ReferenceEquals(target,ClearAllMappingsAction) || ReferenceEquals(target,DeleteSelectedMappingAction)) return;
        if (sender is TextBox { DataContext: KeyMappingEntry row } input) await CommitInputAsync(row.Key,input.Text);
    }
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
