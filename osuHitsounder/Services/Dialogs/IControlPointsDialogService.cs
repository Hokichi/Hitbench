using osuHitsounder.Resources.DataModels;
namespace osuHitsounder.Services.Dialogs;

public interface IControlPointsDialogService
{
    Task ShowAsync(ControlPointsSnapshot initial, CancellationToken token);
}
