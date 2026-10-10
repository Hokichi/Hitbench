using osuHitsounder.Services;

namespace osuHitsounder.Services.Dialogs;

public interface INodePasteDialogService
{
    Task<NodePasteTiming?> ChooseTimingAsync(CancellationToken cancellationToken);
    Task ShowCollisionAsync(CancellationToken cancellationToken);
}
