using System.IO;

namespace Hitbench.Services.FileSystem;

public sealed partial class SampleFileService
{
    public async Task<bool> TryMigrateSavedSamplesAsync(string projectPath, CancellationToken token)
    {
        await mutations.WaitAsync(token);
        try
        {
            return await Task.Run(async () =>
            {
                try
                {
                    var legacy = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(projectPath))!, "Saved Samples");
                    SamplePaths.RejectLinkedPath(legacy);
                    var saved = SamplePaths.SavedRoot(applicationDirectory);
                    if (string.Equals(legacy, saved, StringComparison.OrdinalIgnoreCase) ||
                        saved.StartsWith(legacy + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
                        legacy.StartsWith(saved + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                        throw new ArgumentException("Saved sample migration roots must not overlap.");
                    Directory.CreateDirectory(saved);
                    if (File.Exists(legacy)) throw new IOException("The legacy sample folder is occupied by a file.");
                    return !Directory.Exists(legacy) || await MigrateFolderAsync(legacy, saved, token);
                }
                catch (Exception error) when (IsMigrationFailure(error))
                {
                    System.Diagnostics.Trace.TraceWarning("Saved sample migration failed for {0}: {1}", projectPath, error.Message);
                    return false;
                }
            }, token);
        }
        finally { mutations.Release(); }
    }

    private async Task<bool> MigrateFolderAsync(string source, string destination, CancellationToken token)
    {
        var complete = true;
        foreach (var entry in Directory.EnumerateFileSystemEntries(source).ToArray())
        {
            token.ThrowIfCancellationRequested();
            try
            {
                SamplePaths.RejectLinkedPath(entry);
                SamplePaths.RejectLinkedPath(destination);
                if (Directory.Exists(entry))
                    complete &= await MigrateFolderAsync(entry, AllocateMigrationFolder(destination, Path.GetFileName(entry)), token);
                else
                {
                    await CopyUniqueAsync(entry, destination, token);
                    token.ThrowIfCancellationRequested();
                    SamplePaths.RejectLinkedPath(entry);
                    File.Delete(entry);
                }
            }
            catch (Exception error) when (IsMigrationFailure(error))
            {
                complete = false;
                System.Diagnostics.Trace.TraceWarning("Saved sample was retained at {0}: {1}", entry, error.Message);
            }
        }
        token.ThrowIfCancellationRequested();
        SamplePaths.RejectLinkedPath(source);
        if (!Directory.EnumerateFileSystemEntries(source).Any()) Directory.Delete(source);
        return complete && !Directory.Exists(source);
    }

    private static string AllocateMigrationFolder(string parent, string name)
    {
        for (var suffix = 1; ; suffix++)
        {
            var path = Path.Combine(parent, suffix == 1 ? name : $"{name} ({suffix})");
            var existing = Directory.EnumerateFileSystemEntries(parent).FirstOrDefault(entry => string.Equals(entry, path, StringComparison.OrdinalIgnoreCase));
            if (existing is not null)
            {
                SamplePaths.RejectLinkedPath(existing);
                if (Directory.Exists(existing)) return existing;
                continue;
            }
            SamplePaths.RejectLinkedPath(path);
            return Directory.CreateDirectory(path).FullName;
        }
    }

    private static bool IsMigrationFailure(Exception error) =>
        error is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException;

    public async Task<string> SaveAsync(string projectPath, string fileName, CancellationToken token)
    {
        await mutations.WaitAsync(token);
        try
        {
            return await Task.Run(async () =>
            {
                var source = SamplePaths.Resolve(projectPath, fileName);
                var destination = SamplePaths.SavedRoot(applicationDirectory);
                Directory.CreateDirectory(destination);
                return await CopyUniqueAsync(source, destination, token);
            }, token);
        }
        finally { mutations.Release(); }
    }

    private async Task<string> CopyUniqueAsync(string source, string folder, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        SamplePaths.RejectLinkedPath(source);
        SamplePaths.RejectLinkedPath(folder);
        var temporary = Path.Combine(folder, $".sample-{Guid.NewGuid():N}.tmp");
        try
        {
            await using (var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, true))
            await using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true))
                await copyBytes(input, output, token);
            var name = Path.GetFileName(source);
            var stem = Path.GetFileNameWithoutExtension(name);
            var extension = Path.GetExtension(name);
            for (var suffix = 1; ; suffix++)
            {
                token.ThrowIfCancellationRequested();
                SamplePaths.RejectLinkedPath(folder);
                var candidate = Path.Combine(folder, suffix == 1 ? name : $"{stem} ({suffix}){extension}");
                if (Directory.EnumerateFileSystemEntries(folder).Any(path => string.Equals(path, candidate, StringComparison.OrdinalIgnoreCase))) continue;
                try { File.Move(temporary, candidate); return candidate; }
                catch (IOException) when (File.Exists(candidate) || Directory.Exists(candidate)) { }
            }
        }
        finally
        {
            SamplePaths.RejectLinkedPath(folder);
            File.Delete(temporary);
        }
    }
}
