using System.Windows;
using System.Windows.Input;
using Hitbench.Resources.CustomControls;
using Hitbench.ViewModels.Popups;
namespace Hitbench.Views.Popups;
public partial class OHsTrackAppearancePopup : BasePopup
{
    private readonly OHsTrackAppearancePopupVM viewModel;
    private readonly Action<bool> complete;
    public OHsTrackAppearancePopup(OHsTrackAppearancePopupVM viewModel) : this(viewModel,null) { }
    internal OHsTrackAppearancePopup(OHsTrackAppearancePopupVM viewModel, Action<bool>? complete)
    {
        this.viewModel=viewModel; this.complete=complete ?? (accepted=>DialogResult=accepted);
        InitializeComponent(); DataContext=viewModel;
        Loaded+=(_,_)=>NameInput.Focus();
    }
    protected override bool ShouldBubbleApplicationClose=>false;
    internal bool HandleKey(Key key)
    {
        if(key==Key.Escape){ if(!viewModel.IsDirty)complete(false); return true; }
        if(key==Key.Enter){ if(viewModel.CanSave)complete(true); return true; }
        return false;
    }
    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        if(Keyboard.Modifiers==ModifierKeys.None && HandleKey(e.Key)){ e.Handled=true; return; }
        base.OnPreviewKeyDown(e);
    }
    protected override void OnCloseButtonClick(){ if(!viewModel.IsDirty)complete(false); }
    private void OnCancel(object sender,RoutedEventArgs e)=>complete(false);
    private void OnSave(object sender,RoutedEventArgs e){ if(viewModel.CanSave)complete(true); }
}
