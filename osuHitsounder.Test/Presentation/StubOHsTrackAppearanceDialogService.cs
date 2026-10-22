using osuHitsounder.Resources.DataModels;
using osuHitsounder.Services.Dialogs;
namespace osuHitsounder.Test.Presentation;
public sealed class StubOHsTrackAppearanceDialogService : IOHsTrackAppearanceDialogService
{
    public Func<OHsTrackAppearanceRequest,CancellationToken,Task<OHsTrackAppearanceResult?>> Request { get; set; }=(_,_)=>Task.FromResult<OHsTrackAppearanceResult?>(null);
    public Task<OHsTrackAppearanceResult?> RequestAsync(OHsTrackAppearanceRequest request,CancellationToken token)=>Request(request,token);
}
