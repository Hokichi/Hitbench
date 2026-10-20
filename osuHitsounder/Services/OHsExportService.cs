using System.IO;
using osuHitsounder.Resources.DataModels;
using osuHitsounder.Services.FileSystem;
using osuHitsounder.Services.ProjectFiles;
namespace osuHitsounder.Services;

public sealed class OHsExportService(OHsExportPublisher? publisher = null) : IOHsExportService
{
    private readonly OHsExportPublisher publisher = publisher ?? new();
    public Task<string> ExportAsync(OHsExportRequest request, CancellationToken token) => Task.Run(async () =>
    {
        token.ThrowIfCancellationRequested(); request.Options.Validate();
        static string ValidateSource(string path)
        {
            var full = Path.GetFullPath(path);
            for (var parent = new DirectoryInfo(Path.GetDirectoryName(full)!); parent is not null; parent = parent.Parent) OHsExportPublisher.RejectLink(parent.FullName);
            OHsExportPublisher.RejectLink(full);
            if (!File.Exists(full)) throw new FileNotFoundException("Export source file is missing.", full);
            return full;
        }
        var mapPath = ValidateSource(request.BeatmapPath); var songPath = ValidateSource(request.AudioPath);
        var fingerprints = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        { [mapPath] = OHsExportPublisher.HashFile(mapPath), [songPath] = OHsExportPublisher.HashFile(songPath) };
        var map = OHsExportBeatmap.Parse(await File.ReadAllTextAsync(mapPath, token));
        var project = ProjectDataSerializer.Deserialize(request.ProjectJson);
        var input = OHsExportInputFactory.Create(project, map, request.Options, token);
        foreach (var file in input.Voices.Select(v => v.FileName).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            token.ThrowIfCancellationRequested(); var path = ValidateSource(SamplePaths.Resolve(request.ProjectPath, file));
            fingerprints[path] = OHsExportPublisher.HashFile(path);
        }
        var plan = OHsExportPlanner.Plan(input, token);
        var extension = Path.GetExtension(songPath).ToLowerInvariant();
        if (extension is not (".ogg" or ".wav" or ".mp3")) throw new NotSupportedException("Unsupported song audio format.");
        using (var song = AudioDecoder.Open(songPath)) if (song.Length == 0) throw new InvalidDataException("Song audio is empty.");
        var audioName = "__ohs_export_audio" + extension;
        var difficultyName = request.Options.DiffName + ".osu";
        var generated = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { [audioName] = "song", [difficultyName] = "difficulty" };
        foreach (var bank in plan.Banks)
            foreach (var slot in bank.Value)
                generated.Add(OHsExportRepresentations.FileName(slot.Key, bank.Key, slot.Value.Sources.Count > 1 ? request.Options.MixedFormat : request.Options.SingleFormat), "sample");
        return await publisher.PublishAsync(request.ProjectPath, generated, async (stage, cancellation) =>
        {
            await OHsExportAudio.WriteAsync(request.ProjectPath, stage, plan, request.Options, cancellation);
            await File.WriteAllTextAsync(Path.Combine(stage, difficultyName), OHsExportBeatmapWriter.Write(input, plan, audioName), cancellation);
            await using (var source = File.OpenRead(songPath))
            await using (var output = File.Create(Path.Combine(stage, audioName)))
                await source.CopyToAsync(output, cancellation);
            foreach (var source in fingerprints)
            {
                cancellation.ThrowIfCancellationRequested(); ValidateSource(source.Key);
                if (OHsExportPublisher.HashFile(source.Key) != source.Value) throw new IOException($"Export source changed during preparation: {source.Key}");
            }
        }, token);
    }, token);
}
