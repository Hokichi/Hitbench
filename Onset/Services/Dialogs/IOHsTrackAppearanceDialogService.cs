using Onset.Resources.DataModels;
namespace Onset.Services.Dialogs;
public interface IOHsTrackAppearanceDialogService
{
    Task<OHsTrackAppearanceResult?> RequestAsync(OHsTrackAppearanceRequest request, CancellationToken token);
}
