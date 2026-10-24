using System.IO;

namespace Onset.Services.FileSystem;

public static class SamplePaths
{
    public static string SavedRoot(string projectPath)
    {
        var folder = Path.GetDirectoryName(Path.GetFullPath(projectPath))!;
        for (var current = new DirectoryInfo(folder); current is not null; current = current.Parent)
            RejectLink(current.FullName);
        var saved = Path.Combine(folder, "Saved Samples");
        RejectLink(saved);
        return saved;
    }

    public static bool TryEnsureSavedRoot(string projectPath, CancellationToken token) =>
        TryEnsureSavedRoot(projectPath, token, path => Directory.CreateDirectory(path));

    internal static bool TryEnsureSavedRoot(string projectPath, CancellationToken token, Action<string> createDirectory)
    {
        token.ThrowIfCancellationRequested();
        try
        {
            var path = SavedRoot(projectPath);
            token.ThrowIfCancellationRequested();
            createDirectory(path);
            return true;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            System.Diagnostics.Trace.TraceWarning("Saved Samples folder could not be initialized for {0}: {1}", projectPath, error.Message);
            return false;
        }
    }

    public static string NormalizeFolderName(string trackName)
    {
        var name = new string(trackName.Where(c => char.IsLetterOrDigit(c) || c is ' ' or '_' or '-').ToArray()).Trim();
        if (name.Length == 0) name = "Instrumental";
        if (SampleFileNames.GetStemError(name) == "This filename is reserved by Windows.") name = "_" + name;
        return name[..Math.Min(name.Length, 80)].TrimEnd();
    }
    public static string? GetFolderNameError(string name) => name.Contains('/') || name.Contains('\\') ? "Use a single folder name." : SampleFileNames.GetStemError(name);
    public static string? GetRelativePathError(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || Path.IsPathRooted(path)) return "Use a path relative to Samples.";
        var parts = path.Replace('\\', '/').Split('/');
        foreach (var part in parts) if (SampleFileNames.GetStemError(part) is { } error) return error;
        return SampleFileNames.GetFileNameError(parts[^1]);
    }
    public static string Root(string projectPath)
    {
        var root = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(projectPath))!, "Samples");
        RejectLink(root); return root;
    }
    public static string Resolve(string projectPath, string relativePath)
    {
        if (GetRelativePathError(relativePath) is { } error) throw new ArgumentException(error, nameof(relativePath));
        return ResolveSegments(projectPath, relativePath);
    }
    public static string ResolveFolder(string projectPath, string folderName)
    {
        if (GetFolderNameError(folderName) is { } error) throw new ArgumentException(error, nameof(folderName));
        return ResolveSegments(projectPath, folderName);
    }
    public static string Relative(string projectPath, string path) => Path.GetRelativePath(Root(projectPath), path).Replace('\\', '/');
    private static string ResolveSegments(string projectPath, string relativePath)
    {
        var root = Root(projectPath); var result = root;
        foreach (var part in relativePath.Replace('\\', '/').Split('/')) { result = Path.Combine(result, part); RejectLink(result); }
        result = Path.GetFullPath(result);
        if (!result.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Sample path must stay inside Samples.", nameof(relativePath));
        return result;
    }
    private static void RejectLink(string path)
    {
        if ((File.Exists(path) || Directory.Exists(path)) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) throw new ArgumentException("Linked sample folders and files are not supported.");
    }
}
