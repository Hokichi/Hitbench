using System.IO;
using osuHitsounder.Resources.DataModels;
using osuHitsounder.Services.FileSystem;
using osuHitsounder.Services.ProjectFiles;
namespace osuHitsounder.Services;

public sealed class OHsExportService(OHsExportPublisher? publisher = null, Func<string, CancellationToken, DecodedAudio>? decode = null) : IOHsExportService
{
    private readonly OHsExportPublisher publisher = publisher ?? new();
    public Task<string> ExportAsync(OHsExportRequest request, CancellationToken token, IProgress<OHsExportProgress>? progress = null) => Task.Run(async () =>
    {
        token.ThrowIfCancellationRequested(); request.Options.Validate();
        progress?.Report(new(0, "Validating sources"));
        static string ValidateSource(string path)
        {
            var full = Path.GetFullPath(path);
            for (var parent = new DirectoryInfo(Path.GetDirectoryName(full)!); parent is not null; parent = parent.Parent) OHsExportPublisher.RejectLink(parent.FullName);
            OHsExportPublisher.RejectLink(full);
            if (!File.Exists(full)) throw new FileNotFoundException("Export source file is missing.", full);
            return full;
        }
        var mapPath = ValidateSource(BeatmapTimingReader.FindBeatmap(Path.GetDirectoryName(Path.GetFullPath(request.BeatmapPath))!));
        var sourceName = Path.GetFileNameWithoutExtension(mapPath);
        var close = sourceName.LastIndexOf(']'); var open = sourceName.LastIndexOf('[');
        if (open < 0 || close <= open) throw new InvalidDataException("Source beatmap filename has no difficulty in brackets.");
        var difficultyStem = sourceName[..(open + 1)] + request.Options.DiffName + sourceName[close..];
        if (SampleFileNames.GetStemError(difficultyStem) is { } nameError) throw new InvalidDataException(nameError);
        var difficultyName = difficultyStem + Path.GetExtension(mapPath);
        var fingerprints = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        { [mapPath] = OHsExportPublisher.HashFile(mapPath) };
        var map = OHsExportBeatmap.Parse(await File.ReadAllTextAsync(mapPath, token));
        var project = ProjectDataSerializer.Deserialize(request.ProjectJson);
        var input = OHsExportInputFactory.Create(project, map, request.Options, token);
        var sourcePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in input.Voices.Select(v => v.FileName).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            token.ThrowIfCancellationRequested(); var path = ValidateSource(SamplePaths.Resolve(request.ProjectPath, file));
            fingerprints[path] = OHsExportPublisher.HashFile(path);
            sourcePaths.Add(path);
        }
        progress?.Report(new(10, "Preparing sample combinations"));
        var plan = OHsExportPlanner.Plan(input, token, progress);
        progress?.Report(new(25, "Decoding samples"));
        var decoded = new Dictionary<string, DecodedAudio>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in sourcePaths)
        {
            token.ThrowIfCancellationRequested();
            var audio = (decode ?? ((file, cancellation) => AudioDecoder.Decode(file, cancellation)))(path, token);
            foreach (var value in audio.Samples)
                if (!float.IsFinite(value)) throw new InvalidDataException("Source sample contains invalid PCM.");
            decoded.Add(path, audio);
            progress?.Report(new(25 + 15d * decoded.Count / sourcePaths.Count, "Decoding samples"));
        }
        progress?.Report(new(40, "Generating samples"));
        var generated = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { [difficultyName] = "difficulty" };
        foreach (var bank in plan.Banks)
            foreach (var slot in bank.Value)
                generated.Add(OHsExportRepresentations.FileName(slot.Key, bank.Key, slot.Value.Sources.Count > 1 ? request.Options.MixedFormat : request.Options.SingleFormat), "sample");
        var directory = await publisher.PublishAsync(request.ProjectPath, generated, async (stage, cancellation) =>
        {
            await OHsExportAudio.WriteAsync(request.ProjectPath, stage, plan, request.Options, cancellation, (path, _) => decoded[path], progress);
            await File.WriteAllTextAsync(Path.Combine(stage, difficultyName), OHsExportBeatmapWriter.Write(input, plan), cancellation);
            foreach (var source in fingerprints)
            {
                cancellation.ThrowIfCancellationRequested(); ValidateSource(source.Key);
                if (OHsExportPublisher.HashFile(source.Key) != source.Value) throw new IOException($"Export source changed during preparation: {source.Key}");
            }
            progress?.Report(new(95, "Validating export"));
        }, token, progress);
        progress?.Report(new(100, "Export complete"));
        return directory;
    }, token);
}
