using Soundie.Services.Dialogs;

namespace Soundie.Test.Presentation;

public sealed class StubInstallationDialogService : IInstallationDialogService
{
    public Func<CancellationToken, Task<string?>> Locate { get; set; } = _ => Task.FromResult<string?>(null);
    public Func<CancellationToken, Task> NotFound { get; set; } = _ => Task.CompletedTask;
    public Task<string?> LocateAfterScanAsync(CancellationToken token) => Locate(token);
    public Task ShowNotFoundAsync(CancellationToken token) => NotFound(token);
}

