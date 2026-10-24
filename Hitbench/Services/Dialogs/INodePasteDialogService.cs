namespace Hitbench.Services.Dialogs;

public interface INodePasteDialogService
{
    Task ShowCollisionAsync(CancellationToken cancellationToken);
}
