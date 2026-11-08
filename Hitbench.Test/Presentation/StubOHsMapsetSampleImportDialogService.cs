using Hitbench.Services.Dialogs;

namespace Hitbench.Test.Presentation;

public sealed class StubOHsMapsetSampleImportDialogService : IOHsMapsetSampleImportDialogService
{
    public Func<IReadOnlyList<string>, CancellationToken, Task<IReadOnlyList<string>>> Select { get; set; } =
        (paths, _) => Task.FromResult(paths);
    public Task<IReadOnlyList<string>> SelectAsync(IReadOnlyList<string> paths, CancellationToken token) => Select(paths, token);
}
