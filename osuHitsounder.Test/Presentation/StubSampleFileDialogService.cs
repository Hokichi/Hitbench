using osuHitsounder.Services.Dialogs;

namespace osuHitsounder.Test.Presentation;

public sealed class StubSampleFileDialogService(string? selectedPath) : ISampleFileDialogService
{
    public Task<string?> SelectAsync(CancellationToken token) => Task.FromResult(selectedPath);
}
