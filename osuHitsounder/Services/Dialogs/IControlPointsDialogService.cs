using osuHitsounder.Resources.DataModels;
namespace osuHitsounder.Services.Dialogs;

public interface IControlPointsDialogService
{
    Task ShowAsync(ControlPointsSnapshot initial, CancellationToken token);
    Task<bool> ConfirmReplaceAsync(ControlPointSnapshot selected, int targetTimestamp, int conflictingPointCount, CancellationToken token);
}
