using System.IO;
using Hitbench.Core.Constants;

namespace Hitbench.Services.FileSystem;

public sealed partial class SampleFileService
{
    public async Task<string> ImportKeyAsync(string projectPath, string sourcePath, string folderName, string key, CancellationToken token)
    {
        if (!InstrumentalKeys.TryGetIndex(key, out _)) throw new ArgumentException("Choose C1–B9.", nameof(key));
        if (SampleFileNames.GetFileNameError(Path.GetFileName(sourcePath)) is { } error) throw new InvalidDataException(error);
        var folder = SamplePaths.ResolveFolder(projectPath, folderName);
        var source = Path.GetFullPath(sourcePath);
        SamplePaths.RejectLinkedPath(source);
        await mutations.WaitAsync(token).ConfigureAwait(false);
        string? staged = null, backup = null;
        try
        {
            token.ThrowIfCancellationRequested();
            Directory.CreateDirectory(folder);
            var destination = SamplePaths.Resolve(projectPath, folderName + "/" + key + Path.GetExtension(source));
            var existing = Directory.EnumerateFileSystemEntries(folder).FirstOrDefault(path => string.Equals(path, destination, StringComparison.OrdinalIgnoreCase));
            if (existing is not null)
            {
                SamplePaths.RejectLinkedPath(existing);
                if (Directory.Exists(existing)) throw new IOException("The key sample destination is a folder.");
                if (string.Equals(source, existing, StringComparison.OrdinalIgnoreCase)) return SamplePaths.Relative(projectPath, existing);
            }
            staged = Path.Combine(folder, $".sample-{Guid.NewGuid():N}.tmp");
            await using (var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, true))
            await using (var output = new FileStream(staged, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true))
                await copyBytes(input, output, token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            SamplePaths.RejectLinkedPath(folder);
            SamplePaths.RejectLinkedPath(destination);
            if (existing is not null)
            {
                SamplePaths.RejectLinkedPath(existing);
                backup = Path.Combine(folder, $".sample-backup-{Guid.NewGuid():N}.tmp");
                await Task.Run(() => File.Copy(existing, backup), token).ConfigureAwait(false);
            }
            token.ThrowIfCancellationRequested();
            try
            {
                if (existing is not null) File.Delete(existing);
                finalizeReplacement(staged, destination);
            }
            catch (Exception failure)
            {
                if (backup is not null)
                {
                    try { File.Move(backup, existing!, true); }
                    catch (Exception restoreFailure)
                    {
                        var recovery = backup;
                        backup = null; // Keep recoverable old bytes when restoration cannot complete.
                        throw new IOException($"Could not restore the old sample. Recover it from '{recovery}'.", new AggregateException(failure, restoreFailure));
                    }
                }
                throw;
            }
            return SamplePaths.Relative(projectPath, destination);
        }
        finally
        {
            try { CleanupKeyImportFile(staged); CleanupKeyImportFile(backup); }
            finally { mutations.Release(); }
        }
    }

    private static void CleanupKeyImportFile(string? path)
    {
        if (path is null) return;
        try { SamplePaths.RejectLinkedPath(path); File.Delete(path); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException)
        { System.Diagnostics.Trace.TraceWarning("Could not clean sample staging file {0}: {1}", path, error.Message); }
    }
}
