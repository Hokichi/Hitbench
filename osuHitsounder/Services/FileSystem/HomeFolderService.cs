using System.IO;
using System.IO.Enumeration;
using osuHitsounder.Resources.DataModels;

namespace osuHitsounder.Services.FileSystem;

public sealed class HomeFolderService(string applicationDirectory, Func<CancellationToken, Action, IEnumerable<string>>? installationDirectories = null)
    : IHomeFolderService
{
    public Task<FolderSnapshot> LoadProjectsAsync(CancellationToken cancellationToken) =>
        Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var path = Directory.CreateDirectory(Path.Combine(applicationDirectory, "projects")).FullName;
            return ReadSnapshot(path, cancellationToken);
        }, cancellationToken);

    public Task<FolderSnapshot?> LoadSongsAsync(string osuDirectory, CancellationToken cancellationToken) =>
        Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var songsPath = Path.Combine(osuDirectory, "Songs");
            try { return ReadSnapshot(songsPath, cancellationToken, songs: true); }
            catch (DirectoryNotFoundException) when (!Directory.Exists(songsPath)) { return null; }
        }, cancellationToken);

    public Task<bool> IsInstallationAsync(string directory, CancellationToken cancellationToken) =>
        Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Path.IsPathFullyQualified(directory) &&
                File.Exists(Path.Combine(directory, "osu!.exe"));
        }, cancellationToken);

    private static FolderSnapshot ReadSnapshot(string path, CancellationToken token, bool songs = false)
    {
        var entries = new FileSystemEnumerable<FolderEntry>(path,
            (ref FileSystemEntry entry) => new FolderEntry(
                entry.ToFullPath(), entry.FileName.ToString(), entry.LastWriteTimeUtc),
            new EnumerationOptions { RecurseSubdirectories = false, AttributesToSkip = 0, IgnoreInaccessible = false });
        entries.ShouldIncludePredicate = (ref FileSystemEntry entry) =>
        {
            token.ThrowIfCancellationRequested();
            return entry.IsDirectory;
        };
        return new FolderSnapshot(songs ? ReadSongEntries(entries, token) : entries, token);
    }

    private static IEnumerable<FolderEntry> ReadSongEntries(IEnumerable<FolderEntry> entries, CancellationToken token)
    {
        var options = new EnumerationOptions
        {
            RecurseSubdirectories = false, AttributesToSkip = 0,
            IgnoreInaccessible = false, MatchCasing = MatchCasing.CaseInsensitive
        };
        foreach (var entry in entries)
        {
            token.ThrowIfCancellationRequested();
            string? first = null;
            foreach (var file in Directory.EnumerateFiles(entry.FullName, "*.osu", options))
            {
                token.ThrowIfCancellationRequested();
                if (first is null || StringComparer.Ordinal.Compare(file, first) < 0) first = file;
            }
            if (first is null) continue;
            var metadata = false;
            var artist = "";
            var title = "";
            foreach (var line in File.ReadLines(first))
            {
                token.ThrowIfCancellationRequested();
                var trimmed = line.Trim();
                if (trimmed.StartsWith('['))
                {
                    if (metadata) break;
                    metadata = trimmed == "[Metadata]";
                    continue;
                }
                if (!metadata) continue;
                var separator = line.IndexOf(':');
                if (separator < 0) continue;
                var key = line[..separator].Trim();
                if (key == "ArtistUnicode") artist = line[(separator + 1)..].Trim();
                if (key == "TitleUnicode") title = line[(separator + 1)..].Trim();
            }
            yield return entry with { DisplayName = $"{artist} - {title}" };
        }
    }

    public Task<OsuSearchResult> FindInstallationAsync(CancellationToken cancellationToken) =>
        Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var skipped = false;
            var candidates = (installationDirectories ?? InstalledApplicationEnumerator.Enumerate)(
                cancellationToken, () => skipped = true);
            var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var directory in candidates)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!visited.Add(directory) || !Path.IsPathFullyQualified(directory)) continue;
                if (File.Exists(Path.Combine(directory, "osu!.exe")))
                    return new OsuSearchResult(directory, skipped);
            }
            cancellationToken.ThrowIfCancellationRequested();
            return new OsuSearchResult(null, skipped);
        }, cancellationToken);
}
