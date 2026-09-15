using System.IO;
using System.IO.Enumeration;
using System.Diagnostics;
using System.Text.Json;
using osuHitsounder.Core.Entities;
using osuHitsounder.Resources.DataModels;

namespace osuHitsounder.Services.FileSystem;

public sealed class HomeFolderService(string applicationDirectory, Func<CancellationToken, Action, IEnumerable<string>>? installationDirectories = null)
    : IHomeFolderService
{
    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".bmp", ".gif", ".jpg", ".jpeg", ".png", ".tif", ".tiff"
    };

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
            var songsPath = Path.GetFullPath(Path.Combine(osuDirectory, "Songs"));
            try { return ReadCachedSongs(songsPath, cancellationToken); }
            catch (DirectoryNotFoundException) when (!Directory.Exists(songsPath)) { return null; }
        }, cancellationToken);

    public Task<bool> IsInstallationAsync(string directory, CancellationToken cancellationToken) =>
        Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Path.IsPathFullyQualified(directory) &&
                File.Exists(Path.Combine(directory, "osu!.exe"));
        }, cancellationToken);

    private FolderSnapshot ReadCachedSongs(string path, CancellationToken token)
    {
        var inventory = ReadSnapshot(path, token);
        var folders = inventory.ReadPage(0, inventory.Count);
        var cachePath = Path.Combine(applicationDirectory, "data.json");
        var cache = ReadCache(cachePath, path);
        var known = new HashSet<string>(cache?.Folders ?? [], StringComparer.OrdinalIgnoreCase);
        var current = folders.Select(folder => folder.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var reused = (cache?.Songs ?? []).Where(song => current.Contains(song.Name)).Select(song =>
            new FolderEntry(song.FullName, song.Name, song.LastWriteTimeUtc)
            {
                Artist = song.Artist, Title = song.Title, DisplayName = song.DisplayName,
                BackgroundPath = song.BackgroundPath
            });
        var snapshot = new FolderSnapshot(reused.Concat(ReadSongEntries(
            folders.Where(folder => !known.Contains(folder.Name)), token)), token);
        if (cache is not null && known.Count == current.Count && known.SetEquals(current)) return snapshot;
        var songs = snapshot.ReadPage(0, snapshot.Count).Select(song => new Song(song.FullName,
            song.Name, song.LastWriteTimeUtc, song.Artist, song.Title, song.DisplayName, song.BackgroundPath)).ToArray();
        token.ThrowIfCancellationRequested();
        SaveCache(cachePath, new SongCache(1, path, current.ToArray(), songs), token);
        return snapshot;
    }

    private static SongCache? ReadCache(string cachePath, string songsRoot)
    {
        try
        {
            using var stream = File.OpenRead(cachePath);
            var cache = JsonSerializer.Deserialize<SongCache>(stream);
            if (cache is not { Version: 1, Folders: not null, Songs: not null } ||
                !StringComparer.OrdinalIgnoreCase.Equals(cache.SongsRoot, songsRoot)) return null;
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var name in cache.Folders)
                if (string.IsNullOrWhiteSpace(name) || name != Path.GetFileName(name) ||
                    name is "." or ".." || !names.Add(name)) return null;
            var songNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var song in cache.Songs)
                if (song is null || song.Name is null || !names.Contains(song.Name) || !songNames.Add(song.Name) ||
                    !StringComparer.OrdinalIgnoreCase.Equals(song.FullName, Path.Combine(songsRoot, song.Name)) ||
                    song.Artist is null || song.Title is null || song.DisplayName is null || song.BackgroundPath is null)
                    return null;
            return cache;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or ArgumentException)
        {
            return null;
        }
    }

    private void SaveCache(string cachePath, SongCache cache, CancellationToken token)
    {
        var temporary = cachePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            Directory.CreateDirectory(applicationDirectory);
            using (var stream = File.Create(temporary)) JsonSerializer.Serialize(stream, cache);
            token.ThrowIfCancellationRequested();
            File.Move(temporary, cachePath, overwrite: true);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            Trace.TraceWarning("Song cache could not be saved: {0}", error.Message);
        }
        finally
        {
            try { File.Delete(temporary); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            { Trace.TraceWarning("Song cache temporary file cleanup failed: {0}", error.Message); }
        }
    }

    private static FolderSnapshot ReadSnapshot(string path, CancellationToken token)
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
        return new FolderSnapshot(entries, token);
    }

    private static IEnumerable<FolderEntry> ReadSongEntries(IEnumerable<FolderEntry> entries, CancellationToken token)
    {
        foreach (var entry in entries)
        {
            token.ThrowIfCancellationRequested();
            FolderEntry? song;
            try
            {
                song = ReadSongEntry(entry, token);
            }
            catch (Exception error) when (error is not OperationCanceledException and not OutOfMemoryException)
            {
                token.ThrowIfCancellationRequested();
                continue;
            }

            if (song is not null) yield return song;
        }
    }

    private static FolderEntry? ReadSongEntry(FolderEntry entry, CancellationToken token)
    {
        var options = new EnumerationOptions
        {
            RecurseSubdirectories = false, AttributesToSkip = 0,
            IgnoreInaccessible = false, MatchCasing = MatchCasing.CaseInsensitive
        };
        string? first = null;
        foreach (var file in Directory.EnumerateFiles(entry.FullName, "*.osu", options))
        {
            token.ThrowIfCancellationRequested();
            if (first is null || StringComparer.Ordinal.Compare(file, first) < 0) first = file;
        }
        if (first is null) return null;
        var metadata = false;
        var backgroundEvents = false;
        var artist = "";
        var title = "";
        string? backgroundPath = null;
        foreach (var line in File.ReadLines(first))
        {
            token.ThrowIfCancellationRequested();
            var trimmed = line.Trim();
            if (trimmed == "//Background and Video events")
            {
                backgroundEvents = true;
                continue;
            }
            if (trimmed.StartsWith('['))
            {
                metadata = trimmed == "[Metadata]";
                backgroundEvents = false;
                continue;
            }
            if (backgroundEvents && backgroundPath is null && TryReadBackgroundPath(trimmed, out var relativePath))
                backgroundPath = Path.Combine(entry.FullName, relativePath);
            if (metadata)
            {
                var separator = line.IndexOf(':');
                if (separator < 0) continue;
                var key = line[..separator].Trim();
                if (key == "ArtistUnicode") artist = line[(separator + 1)..].Trim();
                if (key == "TitleUnicode") title = line[(separator + 1)..].Trim();
            }
        }
        backgroundPath ??= Directory.EnumerateFiles(entry.FullName, "*", options)
            .Where(path => ImageExtensions.Contains(Path.GetExtension(path)))
            .Order(StringComparer.Ordinal)
            .First();
        return entry with
        {
            Artist = artist,
            Title = title,
            DisplayName = $"{artist} - {title}",
            BackgroundPath = backgroundPath
        };
    }

    private static bool TryReadBackgroundPath(string line, out string path)
    {
        const string prefix = "0,0,";
        const string suffix = ",0,0";
        if (!line.StartsWith(prefix, StringComparison.Ordinal) ||
            !line.EndsWith(suffix, StringComparison.Ordinal))
        {
            path = "";
            return false;
        }

        path = line[prefix.Length..^suffix.Length].Trim();
        if (path.Length >= 2 && path[0] == '"' && path[^1] == '"')
            path = path[1..^1];
        return path.Length > 0;
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
