using System.IO;
using Hitbench.Services.Dialogs;
namespace Hitbench.Services.FileSystem;

public sealed class OHsMapsetSampleImportService(ISampleFileService files, IOHsMapsetSampleImportDialogService dialogs)
{
    public async Task<SampleImportResult> ImportAsync(string projectPath, string mapsetDirectory, CancellationToken token)
    {
        var offered = await OHsMapsetSamples.FindAsync(mapsetDirectory, token);
        if (offered.Count == 0) return new([], [], false);
        var selected = await dialogs.SelectAsync(offered, token);
        token.ThrowIfCancellationRequested();
        var sources = selected.Where(p => offered.Contains(p, StringComparer.OrdinalIgnoreCase)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (sources.Length == 0) return new([], [], false);
        var existing = (await files.ListAsync(projectPath, token)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        List<string> imported = []; List<string> failures = [];
        foreach (var source in sources)
        {
            if (token.IsCancellationRequested) break;
            try
            {
                SamplePaths.RejectLinkedPath(source);
                if (!File.Exists(source)) throw new FileNotFoundException("Sample no longer exists.", source);
                var target = OHsMapsetSamples.FolderName + "/" + Path.GetFileName(source);
                if (existing.Contains(target)) continue;
                var name = await files.ImportAsync(projectPath, source, token, OHsMapsetSamples.FolderName, skipExisting: true);
                imported.Add(name); existing.Add(name);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { break; }
            catch (Exception error) { failures.Add($"{Path.GetFileName(source)}: {error.Message}"); }
        }
        return new(imported, failures, token.IsCancellationRequested);
    }
}
