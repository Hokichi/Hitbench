using osuHitsounder.Resources.DataModels;
namespace osuHitsounder.Services.Dialogs;
public interface IOHsTrackAppearanceDialogService
{
    Task<OHsTrackAppearanceResult?> RequestAsync(OHsTrackAppearanceRequest request, CancellationToken token);
}
