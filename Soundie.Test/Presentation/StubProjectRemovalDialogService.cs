using Soundie.Services.Dialogs;

namespace Soundie.Test.Presentation;

public sealed class StubProjectRemovalDialogService : IProjectRemovalDialogService
{
    public Func<string, CancellationToken, Task<bool>> Confirm { get; set; } = (_, _) => Task.FromResult(false);
    public Task<bool> ConfirmAsync(string projectName, CancellationToken token) => Confirm(projectName, token);
}
