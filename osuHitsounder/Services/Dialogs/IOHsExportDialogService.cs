using osuHitsounder.Resources.DataModels;
namespace osuHitsounder.Services.Dialogs;
public interface IOHsExportDialogService
{
    Task ShowAsync(OHsExportRequest request, CancellationToken token);
}
