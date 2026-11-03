using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using Hitbench.Resources.DataModels;

namespace Hitbench.Services.FileSystem;

public sealed partial class OHsUpdateService
{
    public void Discard(OHsStagedUpdate update)
    {
        var root = Path.GetFullPath(temporaryDirectory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var workspace = Path.GetFullPath(update.Workspace);
        if (!workspace.StartsWith(root + "hitbench-update-", StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Invalid update workspace.");
        RejectLinkedAncestors(workspace);
        if (Directory.Exists(workspace)) Directory.Delete(workspace, true);
    }

    public async Task<OHsStagedUpdate> StageAsync(OHsUpdateRelease release, IProgress<double> progress, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        RejectLinkedAncestors(applicationDirectory);
        if (release.AssetSize <= 0 || !IsAssetUri(release.AssetUri, release.Version.Tag)) throw new InvalidDataException("Invalid update asset.");
        var workspace = Path.Combine(Path.GetFullPath(temporaryDirectory), "hitbench-update-" + Guid.NewGuid().ToString("N"));
        RejectLinkedAncestors(workspace);
        Directory.CreateDirectory(workspace);
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
            timeout.CancelAfter(TimeSpan.FromMinutes(15));
            var ct = timeout.Token;
            var archivePath = Path.Combine(workspace, "update.zip");
            using var request = new HttpRequestMessage(HttpMethod.Get, release.AssetUri);
            request.Headers.UserAgent.ParseAdd("Hitbench-Updater/1.0");
            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength is long length && length != release.AssetSize) throw new InvalidDataException("Update size mismatch.");
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            await using (var input = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false))
            await using (var output = new FileStream(archivePath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true))
            {
                var buffer = new byte[81920];
                long received = 0;
                int read;
                while ((read = await input.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
                {
                    received += read;
                    if (received > release.AssetSize) throw new InvalidDataException("Update size overrun.");
                    await output.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
                    hash.AppendData(buffer, 0, read);
                    progress.Report(Math.Min(99, received * 100d / release.AssetSize));
                }
                if (received != release.AssetSize) throw new InvalidDataException("Incomplete update download.");
            }
            var digest = Convert.ToHexString(hash.GetHashAndReset());
            if (release.AssetDigest is not null && !string.Equals(release.AssetDigest, "sha256:" + digest, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Update checksum mismatch.");
            progress.Report(100);
            var payload = Directory.CreateDirectory(Path.Combine(workspace, "payload")).FullName;
            await Task.Run(() => Extract(archivePath, payload, ct), ct).ConfigureAwait(false);
            var manifestPath = Path.Combine(payload, "hitbench-release.json");
            var manifest = JsonSerializer.Deserialize<OHsUpdateManifest>(await File.ReadAllTextAsync(manifestPath, ct).ConfigureAwait(false), JsonOptions);
            if (manifest is null || manifest.Version != release.Version.Tag || manifest.Commit != release.SourceCommit || manifest.Files is null || manifest.Files.Count > 10000)
                throw new InvalidDataException("Update identity mismatch.");
            var listed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var file in manifest.Files)
            {
                ValidateRelativePath(file);
                if (!listed.Add(file)) throw new InvalidDataException("Duplicate inventory entry.");
            }
            var actual = Directory.GetFiles(payload, "*", SearchOption.AllDirectories).Select(path => Path.GetRelativePath(payload, path).Replace('\\', '/')).ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (!listed.SetEquals(actual) || !new[] { "Hitbench.exe", "Hitbench.dll", "Hitbench.runtimeconfig.json", "Hitbench.deps.json", "hitbench-release.json" }.All(listed.Contains))
                throw new InvalidDataException("Incomplete application payload.");
            return new(release.Version.Tag, workspace, payload, listed.ToArray());
        }
        catch
        {
            if (Directory.Exists(workspace)) Directory.Delete(workspace, true);
            throw;
        }
    }

    private static void Extract(string archivePath, string payload, CancellationToken token)
    {
        using var archive = ZipFile.OpenRead(archivePath);
        if (archive.Entries.Count > 10000 || archive.Entries.Sum(entry => entry.Length) > 2L * 1024 * 1024 * 1024) throw new InvalidDataException("Update archive too large.");
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in archive.Entries)
        {
            token.ThrowIfCancellationRequested();
            var name = entry.FullName;
            var directory = name.EndsWith('/');
            ValidateRelativePath(directory ? name[..^1] : name);
            if (!seen.Add(name.TrimEnd('/')) || ((entry.ExternalAttributes >> 16) & 0xF000) == 0xA000 ||
                (entry.ExternalAttributes & (int)FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("Linked or duplicate archive entry.");
            var destination = Path.GetFullPath(Path.Combine(payload, name));
            if (!destination.StartsWith(payload + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Escaped archive path.");
            RejectLinkedAncestors(destination);
            if (directory) Directory.CreateDirectory(destination);
            else
            {
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                using var input = entry.Open();
                using var output = new FileStream(destination, FileMode.CreateNew);
                input.CopyTo(output);
                if (output.Length != entry.Length) throw new InvalidDataException("Invalid archive entry size.");
            }
        }
    }

    internal static void ValidateRelativePath(string? name)
    {
        if (string.IsNullOrEmpty(name) || name.Contains('\\') || Path.IsPathRooted(name) || name.Contains(':')) throw new InvalidDataException("Invalid update path.");
        var parts = name.Split('/');
        foreach (var part in parts)
        {
            var stem = part.Split('.')[0];
            if (part.Length == 0 || part is "." or ".." || part.EndsWith('.') || part.EndsWith(' ') || part.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
                System.Text.RegularExpressions.Regex.IsMatch(stem, "\\A(?:CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])\\z", System.Text.RegularExpressions.RegexOptions.IgnoreCase)) throw new InvalidDataException("Invalid update path.");
        }
        if (new[] { "appsettings.json", "data.json", "projects", "saved" }.Contains(parts[0], StringComparer.OrdinalIgnoreCase)) throw new InvalidDataException("Update targets user data.");
    }

    internal static void RejectLinkedAncestors(string path)
    {
        for (var current = Path.GetFullPath(path); current is not null; current = Path.GetDirectoryName(current))
            if ((File.Exists(current) || Directory.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("Linked update path.");
    }
}
