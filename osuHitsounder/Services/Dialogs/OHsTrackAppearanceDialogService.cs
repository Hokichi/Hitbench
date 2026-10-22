using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using osuHitsounder.Resources.DataModels;
using osuHitsounder.Resources.Helpers;
using osuHitsounder.ViewModels.Popups;
using osuHitsounder.Views.Popups;
namespace osuHitsounder.Services.Dialogs;
public sealed class OHsTrackAppearanceDialogService(Func<Window?> getOwner, Dispatcher dispatcher,
 Func<OHsTrackAppearanceRequest,string,OHsTrackAppearancePopupVM> createViewModel,
 Func<OHsTrackAppearancePopupVM,CancellationToken,Task<bool>>? present=null) : IOHsTrackAppearanceDialogService
{
    public async Task<OHsTrackAppearanceResult?> RequestAsync(OHsTrackAppearanceRequest request,CancellationToken token)
    {
        var operation=await dispatcher.InvokeAsync(async()=>
        {
            token.ThrowIfCancellationRequested();
            var owner=getOwner();
            var palette=(SolidColorBrush)(owner?.TryFindResource(OHsTrackPalette.GetResourceKey(request.TrackColorIndex)) ?? Application.Current.FindResource(OHsTrackPalette.GetResourceKey(request.TrackColorIndex)));
            var c=palette.Color; var vm=createViewModel(request,$"#{c.R:X2}{c.G:X2}{c.B:X2}");
            bool accepted;
            if(present is not null) accepted=await present(vm,token);
            else
            {
                var popup=new OHsTrackAppearancePopup(vm); if(owner?.IsVisible==true)popup.Owner=owner;
                using var registration=token.Register(()=>dispatcher.BeginInvoke(()=>{if(popup.IsVisible)popup.DialogResult=false;}));
                token.ThrowIfCancellationRequested(); accepted=popup.ShowDialog()==true;
            }
            token.ThrowIfCancellationRequested(); return accepted?vm.CreateResult():null;
        },DispatcherPriority.Normal,token);
        return await operation;
    }
}
