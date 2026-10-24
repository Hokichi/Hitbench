using System.IO;
using System.Security.Cryptography;
using Newtonsoft.Json.Linq;
using Hitbench.Resources.DataModels;
namespace Hitbench.Services.FileSystem;

public sealed class OHsExportPublisher
{
    private const string Manifest = ".osuhitsounder-export.json";
    private readonly Action<string, string> moveDirectory;
    private readonly Action<string, string> moveFile;
    public OHsExportPublisher() : this(Directory.Move) { }
    internal OHsExportPublisher(Action<string, string> moveDirectory, Action<string, string>? moveFile = null)
    { this.moveDirectory = moveDirectory; this.moveFile = moveFile ?? File.Move; }

    internal static string HashFile(string path)
    { RejectLink(path); using var stream = File.OpenRead(path); return Convert.ToHexString(SHA256.HashData(stream)); }
    internal static void RejectLink(string path)
    {
        if ((File.Exists(path) || Directory.Exists(path)) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new IOException($"Export cannot follow a linked path: {path}");
    }
    private static string Relative(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || Path.IsPathRooted(name) || name.Split('/', '\\').Any(p =>
            p is "." or ".." || p.Length > 255 || p.EndsWith(' ') || p.EndsWith('.') || p.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
            SampleFileNames.GetStemError(Path.GetFileNameWithoutExtension(p)) is not null))
            throw new ArgumentException("Export inventory requires safe relative paths.");
        return name.Replace('\\', '/');
    }
    private static Dictionary<string, string> Tree(string root, CancellationToken token, bool hashContents = true)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        RejectLink(root); if (!Directory.Exists(root)) return result;
        var directories = new Stack<string>(); directories.Push(root);
        while (directories.TryPop(out var directory))
        {
            token.ThrowIfCancellationRequested(); RejectLink(directory);
            foreach (var entry in Directory.EnumerateFileSystemEntries(directory))
            {
                token.ThrowIfCancellationRequested(); RejectLink(entry);
                var relative = Path.GetRelativePath(root, entry).Replace('\\', '/');
                if (Directory.Exists(entry)) { result.Add(relative + "/", "directory"); directories.Push(entry); }
                else result.Add(relative, hashContents ? HashFile(entry) : "file");
            }
        }
        return result;
    }
    public async Task<string> PublishAsync(string projectPath, IReadOnlyDictionary<string, string> generatedFiles, Func<string, CancellationToken, Task> build, CancellationToken token, IProgress<OHsExportProgress>? progress = null, bool overwriteEverything = false)
    {
        token.ThrowIfCancellationRequested();
        var project = Path.GetDirectoryName(Path.GetFullPath(projectPath))!;
        for (var parent = new DirectoryInfo(project); parent is not null; parent = parent.Parent) RejectLink(parent.FullName);
        var inventory = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in generatedFiles)
        {
            var relative = Relative(entry.Key);
            if (relative.Equals(Manifest, StringComparison.OrdinalIgnoreCase) || !inventory.TryAdd(relative, entry.Value)) throw new ArgumentException("Duplicate or reserved export filename.");
        }
        var output = Path.Combine(project, "Export");
        if (File.Exists(output)) throw new IOException("Export is occupied by a file.");
        var before = Tree(output, token);
        var owned = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!overwriteEverything && before.ContainsKey(Manifest))
        {
            JObject manifest;
            try { manifest = JObject.Parse(await File.ReadAllTextAsync(Path.Combine(output, Manifest), token)); }
            catch (Newtonsoft.Json.JsonException error) { throw new IOException("Invalid export manifest.", error); }
            if ((int?)manifest["version"] != 1 || manifest["files"] is not JArray entries) throw new IOException("Unsupported export manifest.");
            foreach (var entry in entries)
            {
                var name = Relative((string?)entry["path"] ?? "");
                if (name.Equals(Manifest, StringComparison.OrdinalIgnoreCase) || !owned.Add(name) || entry["hash"]?.Type != JTokenType.String) throw new IOException("Invalid export ownership entry.");
                if (before.TryGetValue(name, out var hash) && hash != (string?)entry["hash"]) throw new IOException($"Exporter-owned file was modified: {name}");
            }
        }
        foreach (var name in inventory.Keys)
            if (!overwriteEverything && (before.ContainsKey(name) && !owned.Contains(name) || before.ContainsKey(name + "/"))) throw new IOException($"Export filename collision: {name}");
        var stage = Path.Combine(project, ".Export-staging-" + Guid.NewGuid().ToString("N"));
        var backup = Path.Combine(project, ".Export-backup-" + Guid.NewGuid().ToString("N"));
        var published = false; var backedUp = false;
        void Cleanup(string path)
        {
            var full = Path.GetFullPath(path);
            if (!string.Equals(Path.GetDirectoryName(full), project, StringComparison.OrdinalIgnoreCase) ||
                !(Path.GetFileName(full).StartsWith(".Export-staging-", StringComparison.Ordinal) || Path.GetFileName(full).StartsWith(".Export-backup-", StringComparison.Ordinal)))
                throw new IOException("Refusing unsafe export cleanup.");
            Tree(full, CancellationToken.None, hashContents: false); if (Directory.Exists(full)) Directory.Delete(full, true);
        }
        try
        {
            Directory.CreateDirectory(stage);
            foreach (var entry in before)
            {
                token.ThrowIfCancellationRequested();
                if (overwriteEverything || entry.Key.Equals(Manifest, StringComparison.OrdinalIgnoreCase) || owned.Contains(entry.Key)) continue;
                var destination = Path.Combine(stage, entry.Key.Replace('/', Path.DirectorySeparatorChar));
                if (entry.Key.EndsWith('/')) Directory.CreateDirectory(destination);
                else { Directory.CreateDirectory(Path.GetDirectoryName(destination)!); File.Copy(Path.Combine(output, entry.Key), destination); }
            }
            await build(stage, token); token.ThrowIfCancellationRequested();
            var staged = Tree(stage, token);
            foreach (var name in inventory.Keys) if (!staged.TryGetValue(name, out var hash) || hash == "directory") throw new IOException($"Export did not create {name}.");
            var expected = before.Keys.Where(k => !overwriteEverything && !k.EndsWith('/') && !owned.Contains(k) && !k.Equals(Manifest, StringComparison.OrdinalIgnoreCase)).Concat(inventory.Keys).ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (staged.Keys.Any(k => !k.EndsWith('/') && !expected.Contains(k))) throw new IOException("Export generated an unexpected file.");
            var manifest = new JObject { ["version"] = 1, ["files"] = new JArray(inventory.Select(p => new JObject { ["path"] = p.Key, ["role"] = p.Value, ["hash"] = staged[p.Key] })) };
            await File.WriteAllTextAsync(Path.Combine(stage, Manifest), manifest.ToString(), token);
            var current = Tree(output, token);
            if (before.Count != current.Count || before.Any(p => !current.TryGetValue(p.Key, out var hash) || hash != p.Value)) throw new IOException("Export changed while preparing the new version.");
            token.ThrowIfCancellationRequested(); RejectLink(output); RejectLink(stage);
            progress?.Report(new(97, "Publishing export"));
            // Cancellation is intentionally deferred through publication/recovery.
            if (Directory.Exists(output)) { moveDirectory(output, backup); backedUp = true; }
            try { moveDirectory(stage, output); published = true; }
            catch (Exception failure)
            {
                if (backedUp)
                {
                    try { moveDirectory(backup, output); backedUp = false; }
                    catch (Exception recovery) { throw new IOException($"Export publication failed. Previous complete export retained at {backup}", new AggregateException(failure, recovery)); }
                }
                throw;
            }
            if (backedUp)
            {
                try { Cleanup(backup); backedUp = false; }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException) { System.Diagnostics.Trace.TraceWarning("Export backup retained at {0}: {1}", backup, error.Message); }
            }
            return output;
        }
        finally
        {
            // Never clean a recovery backup from a failed publication.
            if (!published && Directory.Exists(stage))
                try { Cleanup(stage); } catch (Exception error) when (error is IOException or UnauthorizedAccessException) { System.Diagnostics.Trace.TraceWarning("Export staging cleanup failed: {0}", error.Message); }
        }
    }

    internal static string ValidateBeatmapDestination(string beatmapPath, IReadOnlyDictionary<string, string> generatedFiles, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var output = Path.GetDirectoryName(Path.GetFullPath(beatmapPath))!;
        for (var parent = new DirectoryInfo(output); parent is not null; parent = parent.Parent) RejectLink(parent.FullName);
        if (!Directory.Exists(output)) throw new DirectoryNotFoundException(output);
        var existing = Directory.EnumerateFileSystemEntries(output).Select(Path.GetFileName).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in generatedFiles.Keys)
        {
            token.ThrowIfCancellationRequested();
            var name = Relative(file);
            if (name.Contains('/') || name.Equals(Manifest, StringComparison.OrdinalIgnoreCase) || !names.Add(name)) throw new ArgumentException("Invalid Beatmap export inventory.");
            if (existing.Contains(name)) throw new IOException($"Export filename collision: {name}");
        }
        return output;
    }
    public async Task<string> PublishBeatmapAsync(string beatmapPath, IReadOnlyDictionary<string, string> generatedFiles, Func<string, CancellationToken, Task> build, CancellationToken token, IProgress<OHsExportProgress>? progress = null)
    {
        var output = ValidateBeatmapDestination(beatmapPath, generatedFiles, token);
        var parent = Path.GetDirectoryName(output) ?? throw new IOException("Cannot stage beside a filesystem root.");
        var stage = Path.Combine(parent, ".Export-staging-" + Guid.NewGuid().ToString("N"));
        var moved = new List<string>();
        Dictionary<string,string>? staged = null;
        var recoveryFailed = false;
        try
        {
            Directory.CreateDirectory(stage);
            await build(stage, token);
            staged = Tree(stage, token);
            if (staged.Count != generatedFiles.Count || generatedFiles.Keys.Any(name => !staged.ContainsKey(name) || staged[name] == "directory")) throw new IOException("Export generated an unexpected or missing file.");
            ValidateBeatmapDestination(beatmapPath, generatedFiles, token);
            token.ThrowIfCancellationRequested();
            progress?.Report(new(97, "Publishing export"));
            try
            {
                foreach (var name in generatedFiles.Keys)
                { RejectLink(output); moveFile(Path.Combine(stage, name), Path.Combine(output, name)); moved.Add(name); }
            }
            catch (Exception failure)
            {
                var errors = new List<Exception> { failure };
                foreach (var name in moved.AsEnumerable().Reverse())
                {
                    try
                    {
                        if (HashFile(Path.Combine(output,name)) != staged[name]) throw new IOException($"Published file changed during recovery: {name}");
                        moveFile(Path.Combine(output,name),Path.Combine(stage,name));
                    }
                    catch (Exception error) { recoveryFailed = true; errors.Add(error); }
                }
                if (recoveryFailed) throw new IOException($"Export recovery failed; recovery files retained at {stage}", new AggregateException(errors));
                throw;
            }
            return output;
        }
        finally
        {
            if (!recoveryFailed && Directory.Exists(stage))
            {
                try
                {
                    if (!string.Equals(Path.GetDirectoryName(Path.GetFullPath(stage)), parent, StringComparison.OrdinalIgnoreCase) || !Path.GetFileName(stage).StartsWith(".Export-staging-", StringComparison.Ordinal)) throw new IOException("Refusing unsafe export cleanup.");
                    Tree(stage, CancellationToken.None, false); Directory.Delete(stage, true);
                }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException) { System.Diagnostics.Trace.TraceWarning("Export staging retained at {0}: {1}",stage,error.Message); }
            }
        }
    }
}
