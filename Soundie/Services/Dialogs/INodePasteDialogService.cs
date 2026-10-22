namespace Soundie.Services.Dialogs;

public interface INodePasteDialogService
{
    Task ShowCollisionAsync(CancellationToken cancellationToken);
}
