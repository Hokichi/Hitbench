namespace osuHitsounder.Services.Dialogs;

public interface ITrackRemovalDialogService
{
    Task<bool> ConfirmAsync(CancellationToken cancellationToken);
}
