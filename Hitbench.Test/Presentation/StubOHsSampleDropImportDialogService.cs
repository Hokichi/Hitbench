using Hitbench.Services.Dialogs;
namespace Hitbench.Test.Presentation;

public sealed class StubOHsSampleDropImportDialogService : IOHsSampleDropImportDialogService
{
    public Func<IReadOnlyList<string>, CancellationToken, Task<IReadOnlyList<string>>> Select { get; set; } = (paths, _) => Task.FromResult(paths);
    public int Calls { get; private set; }
    public Task<IReadOnlyList<string>> SelectAsync(IReadOnlyList<string> paths, CancellationToken token) { Calls++; return Select(paths, token); }
}
