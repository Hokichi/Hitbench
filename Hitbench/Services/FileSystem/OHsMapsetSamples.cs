using System.IO;
using System.Text.RegularExpressions;

namespace Hitbench.Services.FileSystem;

public static class OHsMapsetSamples
{
    public const string FolderName = "Mapset";
    private static readonly Regex Names = new(@"\A(normal|soft|drum)-hit(normal|whistle|finish|clap)[0-9]*\.(wav|ogg|mp3)\z",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);

    public static bool IsMatchingFileName(string fileName) => Names.IsMatch(fileName);

    public static bool IsMapsetSamplePath(string relativePath)
    {
        var parts = relativePath.Replace('\\', '/').Split('/');
        return parts.Length == 2 && string.Equals(parts[0], FolderName, StringComparison.OrdinalIgnoreCase)
            && SampleFileNames.GetFileNameError(parts[1]) is null;
    }

    public static Task<IReadOnlyList<string>> FindAsync(string mapsetDirectory, CancellationToken token) =>
        Task.Run<IReadOnlyList<string>>(() =>
        {
            token.ThrowIfCancellationRequested();
            var root = Path.GetFullPath(mapsetDirectory);
            SamplePaths.RejectLinkedPath(root);
            var matches = new List<string>();
            foreach (var path in Directory.EnumerateFiles(root))
            {
                token.ThrowIfCancellationRequested();
                if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) == 0 && IsMatchingFileName(Path.GetFileName(path)))
                    matches.Add(path);
            }
            return matches.OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(Path.GetFileName, StringComparer.Ordinal).ToArray();
        }, token);
}
