using System.IO;
using Hitbench.Resources.DataModels;

namespace Hitbench.Services.FileSystem;

public sealed class OHsHitNormalSamples(ISampleFileService files)
{
    public async Task<IReadOnlyList<OHsHitNormalChoice>> ListAsync(string projectPath, string mapsetDirectory, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var projectFiles = await files.ListAsync(projectPath, token, includeSubfolders: true);
        var mapFiles = Directory.Exists(mapsetDirectory) ? await OHsMapsetSamples.FindAsync(mapsetDirectory, token) : [];
        token.ThrowIfCancellationRequested();
        return Array.AsReadOnly(new[] { new OHsHitNormalChoice("", "Default") }
            .Concat(projectFiles.Where(p => GetPathError(p) is null && p.Length > 0)
                .Select(p => new OHsHitNormalChoice(p.Replace('\\', '/'), "Samples/" + p.Replace('\\', '/')))
                .Concat(mapFiles.Select(p => new OHsHitNormalChoice(p, p)))
                .DistinctBy(c => c.Path, StringComparer.OrdinalIgnoreCase)
                .OrderBy(c => c.DisplayName, StringComparer.OrdinalIgnoreCase).ThenBy(c => c.DisplayName, StringComparer.Ordinal))
            .ToArray());
    }

    public static string? GetPathError(string path)
    {
        if (path.Length == 0) return null;
        if (Path.IsPathFullyQualified(path))
            return SampleFileNames.GetFileNameError(Path.GetFileName(path));
        if (path.Replace('\\', '/').StartsWith(OHsDefaultSamples.FolderName + "/", StringComparison.OrdinalIgnoreCase))
            return "Skin defaults cannot be configured as custom hitnormal samples.";
        return SamplePaths.GetRelativePathError(path);
    }

    public static string Resolve(string projectPath, string mapsetDirectory, string path)
    {
        if (path.Length == 0 || GetPathError(path) is { }) throw new ArgumentException("Invalid hitnormal sample path.", nameof(path));
        if (!Path.IsPathFullyQualified(path)) return SamplePaths.Resolve(projectPath, path);
        var full = Path.GetFullPath(path);
        if (!string.Equals(Path.GetDirectoryName(full), Path.GetFullPath(mapsetDirectory).TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Hitnormal sample must belong to the current mapset.", nameof(path));
        SamplePaths.RejectLinkedPath(full);
        return full;
    }
}
