using osuHitsounder.Services;
using osuHitsounder.Services.Dialogs;

namespace osuHitsounder.Test.Presentation;

public sealed class StubNodePasteDialogService : INodePasteDialogService
{
    public NodePasteTiming? Choice { get; set; } = NodePasteTiming.PlaybackMarker;
    public int Choices { get; private set; }
    public int Collisions { get; private set; }
    public Func<CancellationToken, Task<NodePasteTiming?>>? Choose { get; set; }
    public Task<NodePasteTiming?> ChooseTimingAsync(CancellationToken token) { Choices++; return Choose?.Invoke(token) ?? Task.FromResult(Choice); }
    public Task ShowCollisionAsync(CancellationToken token) { Collisions++; return Task.CompletedTask; }
}
