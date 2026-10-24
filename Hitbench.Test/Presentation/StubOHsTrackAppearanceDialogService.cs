using Hitbench.Resources.DataModels;
using Hitbench.Services.Dialogs;
namespace Hitbench.Test.Presentation;
public sealed class StubOHsTrackAppearanceDialogService : IOHsTrackAppearanceDialogService
{
    public Func<OHsTrackAppearanceRequest,CancellationToken,Task<OHsTrackAppearanceResult?>> Request { get; set; }=(_,_)=>Task.FromResult<OHsTrackAppearanceResult?>(null);
    public Task<OHsTrackAppearanceResult?> RequestAsync(OHsTrackAppearanceRequest request,CancellationToken token)=>Request(request,token);
}
