using System.Windows;
using System.Windows.Input;
using Hitbench.Resources.CustomControls;
using Hitbench.Resources.Helpers;
using Hitbench.Services.Ui;
using Hitbench.ViewModels.Popups;

namespace Hitbench.Views.Popups;

public partial class OHsHitNormalPopup : BasePopup
{
    private readonly OHsHitNormalPopupVM viewModel;
    private readonly Action<bool>? complete;
    private bool completed;

    public OHsHitNormalPopup(OHsHitNormalPopupVM viewModel) : this(viewModel, null) { }
    internal OHsHitNormalPopup(OHsHitNormalPopupVM viewModel, Action<bool>? complete)
    {
        this.viewModel = viewModel; this.complete = complete;
        InitializeComponent(); DataContext = viewModel;
        Loaded += (_, _) => { RefreshWorkArea(); NormalInput.Focus(); };
        DpiChanged += (_, _) => RefreshWorkArea();
        LocationChanged += (_, _) => { if (IsLoaded) RefreshWorkArea(); };
    }

    protected override bool ShouldBubbleApplicationClose => false;
    private void RefreshWorkArea() => ApplyWorkArea(WindowWorkArea.GetClientSize(Owner ?? this), Math.Max(0, ActualHeight - ContentScroll.ActualHeight));
    internal void ApplyWorkArea(Size area, double chrome = 0)
    {
        if (area.IsEmpty || !double.IsFinite(area.Width) || !double.IsFinite(area.Height) || area.Width <= 0 || area.Height <= 0) return;
        MaxWidth = area.Width; Width = Math.Min(560, area.Width); MaxHeight = area.Height;
        ContentScroll.MaxHeight = Math.Max(0, area.Height - Math.Max(0, chrome));
    }

    internal bool TryComplete(bool accepted)
    {
        if (completed || accepted && !viewModel.CanSave) return false;
        completed = true;
        if (complete is not null) complete(accepted); else if (IsVisible) DialogResult = accepted;
        return true;
    }

    internal bool HandleKey(Key key, DependencyObject? originalSource = null)
    {
        if (NormalInput.IsDropDownOpen || SoftInput.IsDropDownOpen || DrumInput.IsDropDownOpen) return false;
        if (key == Key.Escape) { TryComplete(false); return true; }
        if (key == Key.Enter) { TryComplete(!DependencyObjectTree.IsDescendantOf(originalSource, CancelAction)); return true; }
        return false;
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        if (Keyboard.Modifiers == ModifierKeys.Alt)
        {
            var key = e.Key == Key.System ? e.SystemKey : e.Key;
            switch (key)
            {
                case Key.N: NormalInput.Focus(); break;
                case Key.S: SoftInput.Focus(); break;
                case Key.D: DrumInput.Focus(); break;
                case Key.V: TryComplete(true); break;
                case Key.C: TryComplete(false); break;
                default: base.OnPreviewKeyDown(e); return;
            }
            e.Handled = true; return;
        }
        // Let native ComboBox close its dropdown before treating Enter/Escape as modal actions.
        if (NormalInput.IsDropDownOpen || SoftInput.IsDropDownOpen || DrumInput.IsDropDownOpen) return;
        if (Keyboard.Modifiers == ModifierKeys.None && HandleKey(e.Key, e.OriginalSource as DependencyObject)) e.Handled = true;
        else base.OnPreviewKeyDown(e);
    }

    protected override void OnCloseButtonClick() => TryComplete(false);
    private void OnCancel(object sender, RoutedEventArgs e) => TryComplete(false);
    private void OnSave(object sender, RoutedEventArgs e) => TryComplete(true);
}
