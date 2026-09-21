using System.IO;

namespace osuHitsounder.Services.FileSystem;

public sealed class SampleFileService : ISampleFileService
{
    private static readonly HashSet<string> Extensions = new(StringComparer.OrdinalIgnoreCase) { ".wav", ".ogg", ".mp3" };

    public Task<IReadOnlyList<string>> ListAsync(string projectPath, CancellationToken token) => Task.Run<IReadOnlyList<string>>(() =>
    {
        token.ThrowIfCancellationRequested();
        var folder = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(projectPath))!, "Samples");
        Directory.CreateDirectory(folder);
        return Directory.EnumerateFiles(folder).Where(path => Extensions.Contains(Path.GetExtension(path)))
            .Select(Path.GetFileName).OfType<string>().OrderBy(name => name, StringComparer.OrdinalIgnoreCase).ThenBy(name => name, StringComparer.Ordinal).ToArray();
    }, token);

    public async Task<string> ImportAsync(string projectPath, string sourcePath, CancellationToken token)
    {
        var extension = Path.GetExtension(sourcePath);
        if (!Extensions.Contains(extension)) throw new InvalidDataException("Select a WAV, OGG, or MP3 file.");
        var folder = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(projectPath))!, "Samples");
        Directory.CreateDirectory(folder);
        var stem = Path.GetFileNameWithoutExtension(sourcePath);
        var baseName = Path.GetFileName(sourcePath);
        for (var suffix = 1; ; suffix++)
        {
            token.ThrowIfCancellationRequested();
            var name = suffix == 1 ? baseName : $"{stem} ({suffix}){extension}";
            var destination = Path.Combine(folder, name);
            if (Directory.EnumerateFileSystemEntries(folder).Any(path => string.Equals(Path.GetFileName(path), name, StringComparison.OrdinalIgnoreCase))) continue;
            try
            {
                await using var input = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, true);
                await using var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true);
                try { await input.CopyToAsync(output, token); }
                catch
                {
                    await output.DisposeAsync();
                    File.Delete(destination);
                    throw;
                }
                return name;
            }
            catch (IOException) when (File.Exists(destination) && File.Exists(sourcePath)) { }
        }
    }
}
