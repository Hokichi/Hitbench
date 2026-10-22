using Soundie.Resources.DataModels;
namespace Soundie.Services.Dialogs;
public interface IOHsTrackAppearanceDialogService
{
    Task<OHsTrackAppearanceResult?> RequestAsync(OHsTrackAppearanceRequest request, CancellationToken token);
}
