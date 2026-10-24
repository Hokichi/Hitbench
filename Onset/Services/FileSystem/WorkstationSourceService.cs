using System.IO;
using Onset.Core.Entities;
using Onset.Services.ProjectFiles;

namespace Onset.Services.FileSystem;

public sealed class WorkstationSourceService : IWorkstationSourceService
{
    public Task<WorkstationSource> LoadAsync(string projectPath, CancellationToken token) =>
        Task.Run(() => Load(projectPath, token), token);

    private static WorkstationSource Load(string projectPath, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var projectJson = File.ReadAllText(projectPath);
        var project = ProjectDataSerializer.Deserialize(projectJson);
        if (string.IsNullOrWhiteSpace(project.BeatmapPath))
            throw new InvalidDataException("Project does not identify a song folder.");

        var songFolder = Path.GetFullPath(project.BeatmapPath);
        if (!Directory.Exists(songFolder))
            throw new DirectoryNotFoundException($"Song folder was not found: {songFolder}");

        token.ThrowIfCancellationRequested();
        var beatmapPath = BeatmapTimingReader.FindBeatmap(songFolder);

        string? audioName = null;
        var inGeneral = false;
        foreach (var rawLine in File.ReadLines(beatmapPath))
        {
            token.ThrowIfCancellationRequested();
            var line = rawLine.Trim().TrimStart('\uFEFF');
            if (line.StartsWith('['))
            {
                inGeneral = string.Equals(line, "[General]", StringComparison.Ordinal);
                continue;
            }
            if (!inGeneral) continue;
            var separator = line.IndexOf(':');
            if (separator < 0 || !string.Equals(line[..separator].Trim(), "AudioFilename", StringComparison.Ordinal))
                continue;
            audioName = line[(separator + 1)..].Trim();
            if (audioName.Length >= 2 && audioName[0] == '"' && audioName[^1] == '"')
                audioName = audioName[1..^1];
            break;
        }

        if (string.IsNullOrWhiteSpace(audioName))
            throw new InvalidDataException("The beatmap [General] section has no AudioFilename value.");
        if (Path.IsPathFullyQualified(audioName))
            throw new InvalidDataException("AudioFilename must be relative to the song folder.");

        string audioPath;
        try { audioPath = Path.GetFullPath(Path.Combine(songFolder, audioName)); }
        catch (Exception error) when (error is ArgumentException or NotSupportedException or PathTooLongException)
        {
            throw new InvalidDataException("AudioFilename is not a valid relative path.", error);
        }

        var folderPrefix = songFolder.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!audioPath.StartsWith(folderPrefix, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("AudioFilename points outside the song folder.");
        if (!File.Exists(audioPath))
            throw new FileNotFoundException($"Beatmap audio file was not found: {audioName}", audioPath);
        RejectReparsePoints(songFolder, audioPath);
        BeatmapTimingReader.Populate(project, beatmapPath, token);
        Directory.CreateDirectory(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(projectPath))!, "Samples"));
        SamplePaths.TryEnsureSavedRoot(projectPath, token);
        return new WorkstationSource(beatmapPath, audioPath, project);
    }

    private static void RejectReparsePoints(string root, string path)
    {
        var relative = Path.GetRelativePath(root, path);
        var current = root;
        foreach (var segment in relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
        {
            current = Path.Combine(current, segment);
            if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("AudioFilename resolves through a link or reparse point.");
        }
    }
}
