using System.IO;
using NAudio.Wave;
using Hitbench.Resources.DataModels;

namespace Hitbench.Services.FileSystem;

public sealed partial class SampleBrowserService : ISampleBrowserService, IDisposable
{
    private readonly AudioMemoryBudget memoryBudget;
    private readonly Func<string, WaveStream> openDecoder;
    private readonly Ui.OHsAudioOperationProgress? operationProgress;
    private readonly string? applicationDirectory;
    public SampleBrowserService(Func<string, WaveStream>? openDecoder = null, Ui.OHsAudioOperationProgress? operationProgress = null,
        string? applicationDirectory = null, AudioMemoryBudget? memoryBudget = null)
    {
        this.memoryBudget = memoryBudget ?? new();
        this.openDecoder = openDecoder ?? (path => AudioDecoder.Open(path, this.memoryBudget));
        this.operationProgress = operationProgress; this.applicationDirectory = applicationDirectory;
    }
    public Task<string> GetExistingAncestorAsync(string directory, CancellationToken token) => Task.Run(() =>
    {
        token.ThrowIfCancellationRequested();
        string? candidate = Path.GetFullPath(directory);
        while (candidate is not null)
        {
            token.ThrowIfCancellationRequested();
            if (Directory.Exists(candidate)) return candidate;
            candidate = Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(candidate));
        }
        return ""; // No available ancestor: open This PC rather than enumerate a missing path.
    }, token);

    public Task<string> GetSavedDirectoryAsync(CancellationToken token) => Task.Run(() =>
    {
        token.ThrowIfCancellationRequested();
        var saved = SamplePaths.SavedRoot(applicationDirectory ?? AppContext.BaseDirectory);
        Directory.CreateDirectory(saved);
        token.ThrowIfCancellationRequested();
        return saved;
    }, token);
    private static bool IsSupported(string path) => Path.GetExtension(path).ToLowerInvariant() is ".wav" or ".ogg" or ".mp3";
    public Task<Hitbench.Core.Entities.WaveformData> ReadWaveformAsync(string path, CancellationToken token) => operationProgress is null
        ? ReadWaveformCachedAsync(path, token) : operationProgress.RunAsync("Reading sample...", t => ReadWaveformCachedAsync(path, t), token);

    private Task<Hitbench.Core.Entities.WaveformData> ReadWaveformCoreAsync(string path, CancellationToken token) => Task.Run(() =>
    {
        token.ThrowIfCancellationRequested();
        if (!IsSupported(path)) throw new InvalidDataException("Select a WAV, OGG, or MP3 file.");
        using var reader = openDecoder(Path.GetFullPath(path));
        return new WaveformPeakReader().Read(reader as ISampleProvider ?? reader.ToSampleProvider(), token, memoryBudget);
    }, token);

    public Task<IReadOnlyList<SampleBrowserEntry>> GetRootsAsync(CancellationToken token) => Task.Run<IReadOnlyList<SampleBrowserEntry>>(() =>
        DriveInfo.GetDrives().Select(drive => new SampleBrowserEntry(drive.RootDirectory.FullName, drive.Name, true)).ToArray(), token);

    public Task<string> GetDefaultDirectoryAsync(CancellationToken token) => Task.Run(() =>
    {
        foreach (var folder in new[] { Environment.SpecialFolder.MyMusic, Environment.SpecialFolder.UserProfile })
        {
            token.ThrowIfCancellationRequested();
            var path = Environment.GetFolderPath(folder);
            try
            {
                using var entries = Directory.EnumerateFileSystemEntries(path).GetEnumerator();
                entries.MoveNext();
                return Path.GetFullPath(path);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException) { }
        }
        throw new DirectoryNotFoundException("The Music and user-profile directories are unavailable.");
    }, token);

    public Task<IReadOnlyList<SampleBrowserEntry>> GetEntriesAsync(string directory, CancellationToken token) => Task.Run<IReadOnlyList<SampleBrowserEntry>>(() =>
    {
        var entries = new List<SampleBrowserEntry>();
        foreach (var path in Directory.EnumerateFileSystemEntries(Path.GetFullPath(directory)))
        {
            token.ThrowIfCancellationRequested();
            var isDirectory = (File.GetAttributes(path) & FileAttributes.Directory) != 0;
            if (isDirectory || IsSupported(path)) entries.Add(new(path, Path.GetFileName(path), isDirectory));
        }
        return entries.OrderByDescending(entry => entry.IsDirectory).ThenBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(entry => entry.Name, StringComparer.Ordinal).ToArray();
    }, token);

    public Task<SampleFileMetadata> ReadMetadataAsync(string path, CancellationToken token) => operationProgress is null
        ? ReadMetadataCachedAsync(path, token) : operationProgress.RunAsync("Reading sample...", t => ReadMetadataCachedAsync(path, t), token);

    private Task<SampleFileMetadata> ReadMetadataCoreAsync(string path, CancellationToken token) => Task.Run(() =>
    {
        token.ThrowIfCancellationRequested();
        path = Path.GetFullPath(path);
        if (!IsSupported(path)) throw new InvalidDataException("Select a WAV, OGG, or MP3 file.");
        using var reader = openDecoder(path);
        var result = new SampleFileMetadata(path, Path.GetFileName(path), Path.GetExtension(path).TrimStart('.').ToUpperInvariant(),
            new FileInfo(path).Length, reader.TotalTime, reader.WaveFormat.SampleRate, reader.WaveFormat.Channels);
        token.ThrowIfCancellationRequested();
        return result;
    }, token);
}
