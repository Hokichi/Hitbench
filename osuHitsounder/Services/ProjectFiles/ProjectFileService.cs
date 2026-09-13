using System.IO;
using Newtonsoft.Json;
using osuHitsounder.Core.Entities;

namespace osuHitsounder.Services.ProjectFiles;

public sealed class ProjectFileService(string applicationDirectory) : IProjectFileService
{
    public async Task<string> CreateAsync(DirectoryInfo folder, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        folder.Refresh();
        if (!folder.Exists)
            throw new DirectoryNotFoundException($"Song folder no longer exists: {folder.FullName}");

        var json = JsonConvert.SerializeObject(new ProjectData { BeatmapPath = folder.FullName }, Formatting.Indented);
        var directory = Path.GetFullPath(Path.Combine(applicationDirectory, "projects", folder.Name));
        var target = Path.Combine(directory, "project.json");
        Directory.CreateDirectory(directory);
        if (File.Exists(target))
            throw new IOException($"A project already exists: {target}");

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
