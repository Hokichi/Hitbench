using System.IO;
using Newtonsoft.Json;
using osuHitsounder.Core.Entities;

namespace osuHitsounder.Services.ProjectFiles;

public sealed class ProjectFileService(string applicationDirectory) : IProjectFileService
{
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

        var json = JsonConvert.SerializeObject(new ProjectData { BeatmapPath = folder.FullName }, Formatting.Indented);
        var directory = Path.GetFullPath(Path.Combine(applicationDirectory, "projects", projectName));
        var target = Path.Combine(directory, "project.json");
        if (Directory.Exists(directory) || File.Exists(directory))
            throw new IOException($"A project already exists: {directory}");
        Directory.CreateDirectory(directory);

        var temporary = Path.Combine(directory, $".{Guid.NewGuid():N}.tmp");
        try
        {
            await File.WriteAllTextAsync(temporary, json, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporary, target, overwrite: false);
            return target;
        }
        catch (Exception error)
        {
            try { File.Delete(temporary); }
            catch (Exception cleanupError) when (cleanupError is IOException or UnauthorizedAccessException)
            {
                throw new AggregateException($"Project creation failed and temporary file could not be removed: {temporary}",
                    error, cleanupError);
            }
            throw;
        }
    }
}
