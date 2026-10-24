using System.IO;
using NAudio.Wave;
using Hitbench.Resources.DataModels;

namespace Hitbench.Services.FileSystem;

public sealed class SampleBrowserService(Func<string, WaveStream>? openDecoder = null, Ui.OHsAudioOperationProgress? operationProgress = null) : ISampleBrowserService
{
    private readonly Func<string, WaveStream> openDecoder = openDecoder ?? AudioDecoder.Open;
    private static bool IsSupported(string path) => Path.GetExtension(path).ToLowerInvariant() is ".wav" or ".ogg" or ".mp3";
    public Task<Hitbench.Core.Entities.WaveformData> ReadWaveformAsync(string path, CancellationToken token) => operationProgress is null
        ? ReadWaveformCoreAsync(path, token) : operationProgress.RunAsync("Reading sample...", t => ReadWaveformCoreAsync(path, t), token);

    private Task<Hitbench.Core.Entities.WaveformData> ReadWaveformCoreAsync(string path, CancellationToken token) => Task.Run(() =>
    {
        token.ThrowIfCancellationRequested();
        if (!IsSupported(path)) throw new InvalidDataException("Select a WAV, OGG, or MP3 file.");
        using var reader = openDecoder(Path.GetFullPath(path));
        return new WaveformPeakReader().Read(reader.ToSampleProvider(), token);
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
        ? ReadMetadataCoreAsync(path, token) : operationProgress.RunAsync("Reading sample...", t => ReadMetadataCoreAsync(path, t), token);

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
