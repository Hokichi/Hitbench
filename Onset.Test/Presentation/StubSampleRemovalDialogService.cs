using Onset.Services.Dialogs;

namespace Onset.Test.Presentation;

public sealed class StubSampleRemovalDialogService : ISampleRemovalDialogService
{
    public Func<string, CancellationToken, Task<bool>> Confirm { get; set; } = (_, _) => Task.FromResult(false);
    public Task<bool> ConfirmAsync(string fileName, CancellationToken token) => Confirm(fileName, token);
}
