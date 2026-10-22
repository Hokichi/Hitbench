namespace Soundie.Services.Dialogs;

public interface ISampleRemovalDialogService
{
    Task<bool> ConfirmAsync(string fileName, CancellationToken token);
}
