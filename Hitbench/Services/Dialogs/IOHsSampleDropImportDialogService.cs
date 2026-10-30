namespace Hitbench.Services.Dialogs;

public interface IOHsSampleDropImportDialogService
{
    Task<IReadOnlyList<string>> SelectAsync(IReadOnlyList<string> paths, CancellationToken token);
}
