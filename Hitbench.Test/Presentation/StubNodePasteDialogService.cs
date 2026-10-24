using Hitbench.Services.Dialogs;

namespace Hitbench.Test.Presentation;

public sealed class StubNodePasteDialogService : INodePasteDialogService
{
    public int Collisions { get; private set; }
    public Func<CancellationToken, Task>? Collision { get; set; }
    public Task ShowCollisionAsync(CancellationToken token) { Collisions++; return Collision?.Invoke(token) ?? Task.CompletedTask; }
}
