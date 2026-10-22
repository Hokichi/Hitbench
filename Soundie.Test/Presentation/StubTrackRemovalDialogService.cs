using Soundie.Services.Dialogs;

namespace Soundie.Test.Presentation;

public sealed class StubTrackRemovalDialogService : ITrackRemovalDialogService
{
    public Func<CancellationToken, Task<bool>> Confirm { get; set; } = _ => Task.FromResult(false);
    public int Calls { get; private set; }
    public Task<bool> ConfirmAsync(CancellationToken cancellationToken)
    {
        Calls++;
        return Confirm(cancellationToken);
    }
}
