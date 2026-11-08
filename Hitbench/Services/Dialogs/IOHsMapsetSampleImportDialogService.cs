namespace Hitbench.Services.Dialogs;

public interface IOHsMapsetSampleImportDialogService
{
    Task<IReadOnlyList<string>> SelectAsync(IReadOnlyList<string> paths, CancellationToken token, IReadOnlyCollection<string>? duplicatePaths = null);
}
