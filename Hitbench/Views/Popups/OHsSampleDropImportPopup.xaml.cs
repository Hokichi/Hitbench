using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Hitbench.Resources.CustomControls;
using Hitbench.Resources.DataModels;
using Hitbench.Services.Ui;
using Hitbench.ViewModels.Popups;
namespace Hitbench.Views.Popups;

public partial class OHsSampleDropImportPopup : BasePopup
{
    private readonly OHsSampleDropImportPopupVM vm;
    private readonly Action<bool> complete;
    public OHsSampleDropImportPopup(OHsSampleDropImportPopupVM vm) : this(vm, null) { }
    internal OHsSampleDropImportPopup(OHsSampleDropImportPopupVM vm, Action<bool>? complete)
    {
        InitializeComponent(); this.vm = vm; this.complete = complete ?? (accepted => DialogResult = accepted); DataContext = vm;
        Loaded += OnLoaded; DpiChanged += OnDpiChanged;
    }
    private void OnLoaded(object sender, RoutedEventArgs e) { if (Owner is not null) Owner.SizeChanged += OnOwnerSizeChanged; ResizeToOwner(); FileList.SelectedIndex = vm.Entries.Count > 0 ? 0 : -1; FocusRow(false); }
    private void OnDpiChanged(object sender, DpiChangedEventArgs e) => ResizeToOwner();
    private void OnOwnerSizeChanged(object? sender, SizeChangedEventArgs e) => ResizeToOwner();
    private void ResizeToOwner() { var size = WindowWorkArea.GetClientSize(Owner ?? this); Width = Math.Min(760, Math.Min(size.Width > 0 ? size.Width : 760, Owner?.ActualWidth > 0 ? Owner.ActualWidth : 760)); Height = Math.Min(560, Math.Min(size.Height > 0 ? size.Height : 560, Owner?.ActualHeight > 0 ? Owner.ActualHeight : 560)); }
    public void ReleaseOwner() { if (Owner is not null) Owner.SizeChanged -= OnOwnerSizeChanged; Loaded -= OnLoaded; DpiChanged -= OnDpiChanged; }
    private async void OnWaveformRequested(object? sender, EventArgs e) { try { if (sender is FrameworkElement { DataContext: OHsDroppedSampleEntry entry }) await vm.LoadWaveformAsync(entry); } catch (Exception error) { System.Diagnostics.Trace.TraceWarning("Dropped waveform failed: {0}", error.Message); } }
    private void OnCancel(object sender, RoutedEventArgs e) => complete(false);
    private void OnConfirm(object sender, RoutedEventArgs e) { if (vm.CanConfirm) complete(true); }
    internal bool HandleKey(Key key)
    {
        if (key == Key.Escape) { complete(false); return true; }
        if (key == Key.Enter) { if (CancelAction.IsKeyboardFocusWithin) complete(false); else if (vm.CanConfirm) complete(true); return true; }
        if (key == Key.F6) { if (FileList.IsKeyboardFocusWithin) { if (ImportAction.IsEnabled) ImportAction.Focus(); else CancelAction.Focus(); } else if (ImportAction.IsKeyboardFocusWithin) CancelAction.Focus(); else FocusRow(false); return true; }
        if (key is Key.Up or Key.Down or Key.Home or Key.End) { if (vm.Entries.Count == 0) return true; FileList.SelectedIndex = key switch { Key.Home => 0, Key.End => vm.Entries.Count - 1, Key.Up => Math.Max(0, FileList.SelectedIndex - 1), _ => Math.Min(vm.Entries.Count - 1, FileList.SelectedIndex + 1) }; FocusRow(false); return true; }
        if (key is Key.Left or Key.Right) { FocusRow((key == Key.Right) == (FlowDirection == FlowDirection.LeftToRight)); return true; }
        return false;
    }
    protected override void OnPreviewKeyDown(KeyEventArgs e) { if (HandleKey(e.Key)) { e.Handled = true; return; } base.OnPreviewKeyDown(e); }
    private void FocusRow(bool play)
    {
        if (FileList.SelectedItem is not { } item) return; FileList.ScrollIntoView(item); FileList.UpdateLayout();
        if (FileList.ItemContainerGenerator.ContainerFromItem(item) is DependencyObject container) { var elements = Descendants(container); (play ? elements.OfType<BalloonButton>().FirstOrDefault() as UIElement : elements.OfType<CheckBox>().FirstOrDefault())?.Focus(); }
    }
    private static IEnumerable<DependencyObject> Descendants(DependencyObject parent) { yield return parent; for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++) foreach (var child in Descendants(VisualTreeHelper.GetChild(parent, i))) yield return child; }
}
