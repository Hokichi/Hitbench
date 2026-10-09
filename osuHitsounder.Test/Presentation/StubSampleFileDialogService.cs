using osuHitsounder.Services.Dialogs;

namespace osuHitsounder.Test.Presentation;

public sealed class StubSampleFileDialogService(IReadOnlyList<string> selectedPaths) : ISampleFileDialogService
{
    public StubSampleFileDialogService(string? selectedPath) : this(selectedPath is null ? Array.Empty<string>() : new[] { selectedPath }) { }
    public Func<CancellationToken, Task<IReadOnlyList<string>>>? Selection { get; set; }
    public string? InitialDirectory { get; private set; }
    public Task<IReadOnlyList<string>> SelectAsync(string? initialDirectory, CancellationToken token)
    {
        InitialDirectory = initialDirectory;
        return Selection?.Invoke(token) ?? Task.FromResult(selectedPaths);
    }
}
