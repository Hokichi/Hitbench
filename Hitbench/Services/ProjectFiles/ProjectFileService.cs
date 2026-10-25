using System.IO;
using Hitbench.Core.Entities;
using Hitbench.Services.FileSystem;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json;

namespace Hitbench.Services.ProjectFiles;

public sealed class ProjectFileService(string applicationDirectory) : IProjectFileService
{
    private readonly Func<string, string, CancellationToken, Task> writeProjectAsync = File.WriteAllTextAsync;
    private readonly Action<string, string> publishDirectory = Directory.Move;

    internal ProjectFileService(string applicationDirectory,
        Func<string, string, CancellationToken, Task> writeProjectAsync, Action<string, string> publishDirectory)
        : this(applicationDirectory)
    {
        this.writeProjectAsync = writeProjectAsync;
        this.publishDirectory = publishDirectory;
    }

    public async Task SetPinnedAsync(string projectPath, bool isPinned, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var target = ResolveProjectPath(projectPath);
        var json = await File.ReadAllTextAsync(target, cancellationToken);
        ProjectDataSerializer.Deserialize(json);
        using var reader = new JsonTextReader(new StringReader(json)) { DateParseHandling = DateParseHandling.None };
        var root = JObject.Load(reader);
        var pinned = root.Properties().SingleOrDefault(property =>
            string.Equals(property.Name, "IsPinned", StringComparison.OrdinalIgnoreCase));
        if (pinned is null) root["IsPinned"] = isPinned;
        else pinned.Value = isPinned;
        await SaveAsync(target, root.ToString(), cancellationToken);
    }

    public Task DeleteAsync(string projectPath, CancellationToken cancellationToken) => Task.Run(() =>
    {
        cancellationToken.ThrowIfCancellationRequested();
        var directory = Path.GetDirectoryName(ResolveProjectPath(projectPath))!;
        foreach (var entry in Directory.EnumerateFileSystemEntries(directory, "*", new EnumerationOptions
                 { RecurseSubdirectories = true, AttributesToSkip = 0, IgnoreInaccessible = false }))
        {
            cancellationToken.ThrowIfCancellationRequested();
            RejectLink(entry);
        }
        cancellationToken.ThrowIfCancellationRequested();
        Directory.Delete(directory, recursive: true);
    }, cancellationToken);

    private string ResolveProjectPath(string projectPath)
    {
        var target = Path.GetFullPath(projectPath);
        var root = Path.GetFullPath(Path.Combine(applicationDirectory, "projects"));
        var directory = Path.GetDirectoryName(target)!;
        if (!string.Equals(Path.GetFileName(target), "project.json", StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(Path.GetDirectoryName(directory), root, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Project must be a direct child of the projects folder.", nameof(projectPath));
        RejectLink(Path.GetFullPath(applicationDirectory));
        RejectLink(root);
        RejectLink(directory);
        RejectLink(target);
        if (!Directory.Exists(directory)) throw new DirectoryNotFoundException(directory);
        return target;
    }

    private static void RejectLink(string path)
    {
        if ((File.Exists(path) || Directory.Exists(path)) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new ArgumentException("Linked project files and folders are not supported.");
    }

    public async Task SaveAsync(string projectPath, string json, CancellationToken cancellationToken)
    {
        var target = Path.GetFullPath(projectPath);
        var temporary = Path.Combine(Path.GetDirectoryName(target)!, $".{Guid.NewGuid():N}.tmp");
        try
        {
            await File.WriteAllTextAsync(temporary, json, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporary, target, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
    public Task<IReadOnlySet<string>> GetExistingNamesAsync(CancellationToken cancellationToken) =>
        Task.Run<IReadOnlySet<string>>(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var projects = Path.Combine(applicationDirectory, "projects");
            if (!Directory.Exists(projects)) return new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var directory in Directory.EnumerateDirectories(projects, "*", SearchOption.TopDirectoryOnly))
            {
                cancellationToken.ThrowIfCancellationRequested();
                names.Add(Path.GetFileName(directory));
            }
            return names;
        }, cancellationToken);

    public async Task<string> CreateAsync(DirectoryInfo folder, string projectName, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var syntaxError = ProjectNameRules.GetSyntaxError(projectName);
        if (syntaxError is not null) throw new ArgumentException(syntaxError, nameof(projectName));
        projectName = projectName.Trim();

        folder.Refresh();
        if (!folder.Exists)
            throw new DirectoryNotFoundException($"Song folder no longer exists: {folder.FullName}");

        var project = new ProjectData { BeatmapPath = folder.FullName };
        await Task.Run(() =>
        {
            var beatmapPath = BeatmapTimingReader.FindBeatmap(folder.FullName);
            BeatmapTimingReader.Populate(project, beatmapPath, cancellationToken);
        }, cancellationToken);
        var json = ProjectDataSerializer.Serialize(project);
        var applicationRoot = Path.GetFullPath(applicationDirectory);
        for (var parent = new DirectoryInfo(applicationRoot); parent is not null; parent = parent.Parent)
            RejectLink(parent.FullName);
        var projects = Path.Combine(applicationRoot, "projects");
        RejectLink(projects);
        var directory = Path.Combine(projects, projectName);
        var target = Path.Combine(directory, "project.json");
        if (Directory.Exists(directory) || File.Exists(directory))
            throw new IOException($"A project already exists: {directory}");

        var staging = Path.Combine(applicationRoot, $".project-staging-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(projects);
            Directory.CreateDirectory(staging);
            var stagedProject = Path.Combine(staging, "project.json");
            await writeProjectAsync(stagedProject, json, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            RejectLink(projects);
            RejectLink(staging);
            publishDirectory(staging, directory);
            return target;
        }
        catch (Exception error)
        {
            try { DeleteStaging(applicationRoot, staging); }
            catch (Exception cleanupError) when (cleanupError is IOException or UnauthorizedAccessException or ArgumentException)
            {
                throw new AggregateException($"Project creation failed; staging retained at {staging}",
                    error, cleanupError);
            }
            throw;
        }
    }

    private static void DeleteStaging(string applicationRoot, string staging)
    {
        var full = Path.GetFullPath(staging);
        if (!string.Equals(Path.GetDirectoryName(full), applicationRoot, StringComparison.OrdinalIgnoreCase) ||
            !Path.GetFileName(full).StartsWith(".project-staging-", StringComparison.Ordinal))
            throw new IOException("Refusing unsafe project staging cleanup.");
        for (var parent = new DirectoryInfo(applicationRoot); parent is not null; parent = parent.Parent)
            RejectLink(parent.FullName);
        RejectLink(full);
        if (!Directory.Exists(full)) return;
        var pending = new Stack<string>();
        pending.Push(full);
        while (pending.TryPop(out var directory))
            foreach (var entry in Directory.EnumerateFileSystemEntries(directory))
            {
                RejectLink(entry);
                if (Directory.Exists(entry)) pending.Push(entry);
            }
        Directory.Delete(full, recursive: true);
    }
}
