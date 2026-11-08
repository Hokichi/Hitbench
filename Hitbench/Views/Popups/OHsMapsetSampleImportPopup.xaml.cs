using System.Windows;
using System.Windows.Input;
using Hitbench.Resources.CustomControls;
using Hitbench.ViewModels.Popups;
using Hitbench.Services.Ui;
namespace Hitbench.Views.Popups;
public partial class OHsMapsetSampleImportPopup : BasePopup
{
    private readonly OHsMapsetSampleImportPopupVM vm;
    private readonly Action<bool>? complete;
    public OHsMapsetSampleImportPopup(OHsMapsetSampleImportPopupVM vm) : this(vm, null) { }
    internal OHsMapsetSampleImportPopup(OHsMapsetSampleImportPopupVM vm, Action<bool>? complete)
    {
        InitializeComponent(); this.vm = vm; this.complete = complete; DataContext = vm;
        Loaded += OnLoaded;
        Closed += (_, _) => { vm.TryCompleteSelection(false); ReleaseOwner(); };
        DpiChanged += (_, _) => Resize();
    }
    private void OnLoaded(object sender, RoutedEventArgs e) { if (Owner is not null) Owner.SizeChanged += OnOwnerSizeChanged; Resize(); NoAction.Focus(); }
    private void OnOwnerSizeChanged(object? sender, SizeChangedEventArgs e) => Resize();
    private void Resize()
    {
        var area = WindowWorkArea.GetClientSize(Owner ?? this);
        Width = Limit(area.Width, Owner?.RenderSize.Width ?? 0, 640);
        Height = Limit(area.Height, Owner?.RenderSize.Height ?? 0, 480);
        static double Limit(double workArea, double owner, double maximum) =>
            Math.Min(double.IsFinite(workArea) && workArea > 0 ? Math.Min(workArea, maximum) : maximum,
                double.IsFinite(owner) && owner > 0 ? Math.Min(owner, maximum) : maximum);
    }
    public void ReleaseOwner() { if (Owner is not null) Owner.SizeChanged -= OnOwnerSizeChanged; Loaded -= OnLoaded; }
    internal bool TryComplete(bool accepted)
    {
        if (!vm.TryCompleteSelection(accepted)) return false;
        if (complete is not null) complete(accepted); else if (IsVisible) DialogResult = accepted;
        return true;
    }
    private void OnYes(object sender, RoutedEventArgs e) => TryComplete(true);
    private void OnNo(object sender, RoutedEventArgs e) => TryComplete(false);
    internal bool HandleKey(Key key)
    {
        if (key == Key.Escape) { TryComplete(false); return true; }
        if (key == Key.Enter) { TryComplete(YesAction.IsKeyboardFocusWithin && vm.CanConfirm); return true; }
        return false;
    }
    protected override void OnPreviewKeyDown(KeyEventArgs e) { if (HandleKey(e.Key)) e.Handled = true; else base.OnPreviewKeyDown(e); }
}
