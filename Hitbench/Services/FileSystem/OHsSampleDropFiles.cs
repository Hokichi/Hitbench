using System.IO;
namespace Hitbench.Services.FileSystem;

public static class OHsSampleDropFiles
{
    public static Task<IReadOnlyList<string>> GetEligibleAsync(IReadOnlyList<string> paths, CancellationToken token) => Task.Run<IReadOnlyList<string>>(() =>
    {
        var result = new List<string>(); var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in paths) { token.ThrowIfCancellationRequested(); try { var full = Path.GetFullPath(path); if (File.Exists(full) && SampleFileNames.GetFileNameError(Path.GetFileName(full)) is null && seen.Add(full)) result.Add(full); } catch (Exception e) when (e is ArgumentException or NotSupportedException or IOException or UnauthorizedAccessException) { } }
        return result.AsReadOnly();
    }, token);
}
