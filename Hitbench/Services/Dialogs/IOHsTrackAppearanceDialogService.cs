using Hitbench.Resources.DataModels;
namespace Hitbench.Services.Dialogs;
public interface IOHsTrackAppearanceDialogService
{
    Task<OHsTrackAppearanceResult?> RequestAsync(OHsTrackAppearanceRequest request, CancellationToken token);
}
