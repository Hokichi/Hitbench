using System.Collections.ObjectModel;
using System.IO;
using Hitbench.Core.Constants;

namespace Hitbench.Services.FileSystem;

public sealed partial class SampleFileService : ISampleFileService
{
    private readonly string applicationDirectory;
    private readonly Func<Stream, Stream, CancellationToken, Task> copyBytes;
    private readonly Action<string, string> finalizeReplacement;
    public SampleFileService(string? applicationDirectory = null)
        : this(applicationDirectory ?? AppContext.BaseDirectory, (input, output, token) => input.CopyToAsync(output, token)) { }
    internal SampleFileService(string applicationDirectory, Func<Stream, Stream, CancellationToken, Task> copyBytes, Action<string, string>? finalizeReplacement = null)
    {
        this.applicationDirectory = Path.GetFullPath(applicationDirectory);
        this.copyBytes = copyBytes;
        this.finalizeReplacement = finalizeReplacement ?? File.Move;
    }
    private static readonly HashSet<string> Extensions = new(StringComparer.OrdinalIgnoreCase) { ".wav", ".ogg", ".mp3" };
    private readonly SemaphoreSlim mutations = new(1, 1);

    public Task<string> RenameAsync(string projectPath, string fileName, string newStem, CancellationToken token) => MutateAsync(() =>
    {
        if (SampleFileNames.GetStemError(newStem) is { } error) throw new ArgumentException(error, nameof(newStem));
        var original = SamplePaths.Resolve(projectPath, fileName);
        var folder = Path.GetDirectoryName(original)!;
        original = Directory.EnumerateFiles(folder).FirstOrDefault(path => string.Equals(Path.GetFileName(path), Path.GetFileName(original), StringComparison.OrdinalIgnoreCase))
            ?? throw new FileNotFoundException("Sample file was not found.", fileName);
        var destination = Path.Combine(folder, newStem + Path.GetExtension(original));
        if (original == destination) return SamplePaths.Relative(projectPath, original);
        if (Directory.EnumerateFileSystemEntries(folder).Any(path => path != original && string.Equals(path, destination, StringComparison.OrdinalIgnoreCase)))
            throw new IOException("A file or folder with that name already exists.");
        token.ThrowIfCancellationRequested(); File.Move(original, destination);
        return SamplePaths.Relative(projectPath, destination);
    }, token);

    public Task DeleteAsync(string projectPath, string fileName, CancellationToken token) => MutateAsync(() =>
    {
        File.Delete(SamplePaths.Resolve(projectPath, fileName)); return true;
    }, token);

    public Task<IReadOnlyList<string>> ListAsync(string projectPath, CancellationToken token, bool includeSubfolders = true) => Task.Run<IReadOnlyList<string>>(() =>
    {
        token.ThrowIfCancellationRequested();
        var root = SamplePaths.Root(projectPath); Directory.CreateDirectory(root);
        var paths = Directory.EnumerateFiles(root, "*", new EnumerationOptions { RecurseSubdirectories = includeSubfolders, AttributesToSkip = FileAttributes.ReparsePoint, IgnoreInaccessible = false });
        return paths.Where(path => Extensions.Contains(Path.GetExtension(path)))
            .Select(path => { token.ThrowIfCancellationRequested(); return SamplePaths.Relative(projectPath, path); })
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase).ThenBy(name => name, StringComparer.Ordinal).ToArray();
    }, token);

    public async Task<string> ImportAsync(string projectPath, string sourcePath, CancellationToken token, string? folderName = null)
    {
        if (SampleFileNames.GetFileNameError(Path.GetFileName(sourcePath)) is { } error) throw new InvalidDataException(error);
        var folder = folderName is null ? SamplePaths.Root(projectPath) : SamplePaths.ResolveFolder(projectPath, folderName);
        Directory.CreateDirectory(folder);
        await mutations.WaitAsync(token);
        try
        {
            var source = Path.GetFullPath(sourcePath);
            if (string.Equals(Path.GetDirectoryName(source), folder, StringComparison.OrdinalIgnoreCase) && File.Exists(source))
                return SamplePaths.Relative(projectPath, source);
            return SamplePaths.Relative(projectPath, await CopyUniqueAsync(source, folder, token));
        }
        finally { mutations.Release(); }
    }

    public Task<string> EnsureFolderAsync(string projectPath, string trackName, string currentFolderName, IReadOnlyCollection<string> reservedFolderNames, CancellationToken token) => MutateAsync(() =>
    {
        var name = currentFolderName.Length > 0 ? currentFolderName : AllocateFolder(projectPath, trackName, "", reservedFolderNames);
        Directory.CreateDirectory(SamplePaths.ResolveFolder(projectPath, name)); return name;
    }, token);

    public Task<string> RenameFolderAsync(string projectPath, string folderName, string trackName, IReadOnlyCollection<string> reservedFolderNames, CancellationToken token) => MutateAsync(() =>
    {
        var original = SamplePaths.ResolveFolder(projectPath, folderName);
        var name = AllocateFolder(projectPath, trackName, folderName, reservedFolderNames);
        if (name == folderName) return name;
        var destination = SamplePaths.ResolveFolder(projectPath, name); token.ThrowIfCancellationRequested();
        if (string.Equals(name, folderName, StringComparison.OrdinalIgnoreCase))
        {
            var temporary = SamplePaths.ResolveFolder(projectPath, "_rename_" + Guid.NewGuid().ToString("N"));
            Directory.Move(original, temporary);
            try { Directory.Move(temporary, destination); }
            catch { Directory.Move(temporary, original); throw; }
        }
        else Directory.Move(original, destination);
        return name;
    }, token);

    public Task DeleteEmptyFolderAsync(string projectPath, string folderName, CancellationToken token) => MutateAsync(() =>
    {
        var folder = SamplePaths.ResolveFolder(projectPath, folderName);
        if (Directory.Exists(folder) && !Directory.EnumerateFileSystemEntries(folder).Any()) Directory.Delete(folder);
        return true;
    }, token);

    public Task<(IReadOnlyDictionary<string, IReadOnlyList<string>> Candidates, IReadOnlyList<string> IgnoredPaths)> FindNoteFilesAsync(string sourceDirectory, CancellationToken token) => Task.Run(() =>
    {
        var candidates = new Dictionary<string, List<string>>(StringComparer.Ordinal); var ignored = new List<string>();
        foreach (var path in Directory.EnumerateFileSystemEntries(Path.GetFullPath(sourceDirectory)).OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase).ThenBy(Path.GetFileName, StringComparer.Ordinal))
        {
            token.ThrowIfCancellationRequested();
            var stem = Path.GetFileNameWithoutExtension(path);
            var key = InstrumentalKeys.All.FirstOrDefault(note => stem.StartsWith(note, StringComparison.OrdinalIgnoreCase));
            if (!File.Exists(path) || !Extensions.Contains(Path.GetExtension(path)) || key is null) { ignored.Add(path); continue; }
            if (!candidates.TryGetValue(key, out var group)) candidates[key] = group = [];
            group.Add(path);
        }
        return ((IReadOnlyDictionary<string, IReadOnlyList<string>>)new ReadOnlyDictionary<string, IReadOnlyList<string>>(candidates.ToDictionary(pair => pair.Key, pair => (IReadOnlyList<string>)Array.AsReadOnly(pair.Value.ToArray()))),
            (IReadOnlyList<string>)Array.AsReadOnly(ignored.ToArray()));
    }, token);

    private static string AllocateFolder(string projectPath, string trackName, string ownFolder, IReadOnlyCollection<string> reserved)
    {
        var root = SamplePaths.Root(projectPath); Directory.CreateDirectory(root); var stem = SamplePaths.NormalizeFolderName(trackName);
        for (var suffix = 1; ; suffix++)
        {
            var name = suffix == 1 ? stem : $"{stem} ({suffix})";
            if (reserved.Contains(name, StringComparer.OrdinalIgnoreCase)) continue;
            if (string.Equals(name, ownFolder, StringComparison.OrdinalIgnoreCase)) return name;
            if (!Directory.EnumerateFileSystemEntries(root).Any(path => string.Equals(Path.GetFileName(path), name, StringComparison.OrdinalIgnoreCase))) return name;
        }
    }
    private async Task<T> MutateAsync<T>(Func<T> action, CancellationToken token)
    {
        await mutations.WaitAsync(token);
        try { return await Task.Run(() => { token.ThrowIfCancellationRequested(); return action(); }, token); }
        finally { mutations.Release(); }
    }
}
