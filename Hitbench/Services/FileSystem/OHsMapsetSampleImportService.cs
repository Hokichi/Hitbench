using System.IO;
using Hitbench.Services.Dialogs;
namespace Hitbench.Services.FileSystem;

public sealed class OHsMapsetSampleImportService(ISampleFileService files, IOHsMapsetSampleImportDialogService dialogs)
{
    public async Task<SampleImportResult> ImportAsync(string projectPath, string mapsetDirectory, CancellationToken token)
    {
        var offered = await OHsMapsetSamples.FindAsync(mapsetDirectory, token);
        if (offered.Count == 0) return new([], [], false);
        var existing = (await files.ListAsync(projectPath, token)).Where(OHsMapsetSamples.IsMapsetSamplePath)
            .Select(p => Path.GetFileName(p.Replace('\\', '/'))).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var duplicates = offered.Where(p => existing.Contains(Path.GetFileName(p))).ToArray();
        var selected = await dialogs.SelectAsync(offered, token, duplicates);
        token.ThrowIfCancellationRequested();
        var sources = selected.Where(p => offered.Contains(p, StringComparer.OrdinalIgnoreCase)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (sources.Length == 0) return new([], [], false);
        List<string> imported = []; List<string> failures = [];
        foreach (var source in sources)
        {
            if (token.IsCancellationRequested) break;
            try
            {
                SamplePaths.RejectLinkedPath(source);
                if (!File.Exists(source)) throw new FileNotFoundException("Sample no longer exists.", source);
                var target = OHsMapsetSamples.FolderName + "/" + Path.GetFileName(source);
                var overwrite = duplicates.Contains(source, StringComparer.OrdinalIgnoreCase);
                var name = await files.ImportAsync(projectPath, source, token, OHsMapsetSamples.FolderName, skipExisting: !overwrite, overwriteExisting: overwrite);
                imported.Add(name);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { break; }
            catch (Exception error) { failures.Add($"{Path.GetFileName(source)}: {error.Message}"); }
        }
        return new(imported, failures, token.IsCancellationRequested);
    }
}
