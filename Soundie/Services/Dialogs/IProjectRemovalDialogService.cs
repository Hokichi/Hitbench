namespace Soundie.Services.Dialogs;

public interface IProjectRemovalDialogService
{
    Task<bool> ConfirmAsync(string projectName, CancellationToken token);
}
