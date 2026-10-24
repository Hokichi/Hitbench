using System.IO;

namespace Hitbench.Services.FileSystem;

public sealed class SampleImportBatch(ISampleFileService files)
{
    public async Task<SampleImportResult> ImportAsync(string projectPath, IReadOnlyList<string> paths, CancellationToken token)
    {
        List<string> imported = []; List<string> failures = [];
        foreach (var path in paths.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (token.IsCancellationRequested) return new(imported, failures, true);
            try { imported.Add(await files.ImportAsync(projectPath, path, token)); }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { return new(imported, failures, true); }
            catch (Exception error) { failures.Add($"{Path.GetFileName(path)}: {error.Message}"); }
        }
        return new(imported, failures, token.IsCancellationRequested);
    }
}
