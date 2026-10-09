using osuHitsounder.Services.Dialogs;

namespace osuHitsounder.Test.Presentation;

public sealed class StubSampleFileDialogService(IReadOnlyList<string> selectedPaths) : ISampleFileDialogService
{
    public StubSampleFileDialogService(string? selectedPath) : this(selectedPath is null ? Array.Empty<string>() : new[] { selectedPath }) { }
    public Func<CancellationToken, Task<IReadOnlyList<string>>>? Selection { get; set; }
    public Task<IReadOnlyList<string>> SelectAsync(CancellationToken token) => Selection?.Invoke(token) ?? Task.FromResult(selectedPaths);
}
