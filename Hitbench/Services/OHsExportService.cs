using System.IO;
using System.Text;
using Hitbench.Resources.DataModels;
using Hitbench.Services.FileSystem;
using Hitbench.Services.ProjectFiles;
namespace Hitbench.Services;

public sealed class OHsExportService(OHsExportPublisher? publisher = null, Func<string, CancellationToken, DecodedAudio>? decode = null, AudioMemoryBudget? memoryBudget = null) : IOHsExportService
{
    private static (string Text, Encoding Encoding) DecodeBeatmap(byte[] bytes)
    {
        Encoding[] encodings = [new UTF32Encoding(false, true, true), new UTF32Encoding(true, true, true), new UTF8Encoding(true, true), new UnicodeEncoding(false, true, true), new UnicodeEncoding(true, true, true)];
        foreach (var encoding in encodings)
        {
            var preamble = encoding.GetPreamble();
            if (bytes.AsSpan().StartsWith(preamble))
                return (encoding.GetString(bytes, preamble.Length, bytes.Length - preamble.Length), encoding);
        }
        var utf8 = new UTF8Encoding(false, true);
        return (utf8.GetString(bytes), utf8);
    }

    private readonly OHsExportPublisher publisher = publisher ?? new();
    private readonly AudioMemoryBudget memoryBudget = memoryBudget ?? new();
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
        var (sourceText, sourceEncoding) = DecodeBeatmap(await File.ReadAllBytesAsync(mapPath, token));
        var map = OHsExportBeatmap.Parse(sourceText);
        var project = ProjectDataSerializer.Deserialize(request.ProjectJson);
        var cache = SamplePaths.ResolveFolder(request.ProjectPath, OHsDefaultSamples.FolderName);
        var defaults = project.AllowDefaults && Directory.Exists(cache) ? Directory.EnumerateFiles(cache).Select(path => SamplePaths.Relative(request.ProjectPath, path)).ToArray() : [];
        var input = OHsExportInputFactory.Create(project, map, request.Options, token, defaults, request.DurationMilliseconds ?? double.PositiveInfinity);
        progress?.Report(new(5, "Optimizing sample reuse"));
        var plan = OHsExportPlanner.Plan(input, token, progress);
        var sourcePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in plan.Banks.Values.SelectMany(bank => bank.Values).SelectMany(asset => asset.Sources).Select(source => source.FileName).Concat(input.Voices.Where(v => !v.IsDefault).Select(v => v.FileName)).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            token.ThrowIfCancellationRequested(); var path = ValidateSource(SamplePaths.Resolve(request.ProjectPath, file));
            fingerprints[path] = OHsExportPublisher.HashFile(path);
            sourcePaths.Add(path);
        }
        if (request.Options.Destination == OHsExportDestination.Beatmap)
            OHsExportPublisher.ValidateBeatmapDestination(request.BeatmapPath,
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { [difficultyName] = "difficulty" }, token);
        progress?.Report(new(35, "Decoding samples"));
        var decoded = new Dictionary<string, DecodedAudio>(StringComparer.OrdinalIgnoreCase);
        try
        {
            foreach (var path in sourcePaths)
            {
                token.ThrowIfCancellationRequested();
                var audio = decode is null ? AudioDecoder.Decode(path, token, budget: memoryBudget) : decode(path, token);
                try
                {
                    audio.AttachBudget(memoryBudget);
                    foreach (var value in audio.Samples)
                        if (!float.IsFinite(value)) throw new InvalidDataException("Source sample contains invalid PCM.");
                    decoded.Add(path, audio);
                }
                catch { audio.Dispose(); throw; }
                progress?.Report(new(35 + 5d * decoded.Count / sourcePaths.Count, "Decoding samples"));
            }

            var generated = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { [difficultyName] = "difficulty" };
            foreach (var bank in plan.Banks)
                foreach (var slot in bank.Value)
                    generated.Add(OHsExportRepresentations.FileName(slot.Key, bank.Key, slot.Value.Sources.Count > 1 ? request.Options.MixedFormat : request.Options.SingleFormat), "sample");
            if (request.Options.Destination == OHsExportDestination.Beatmap) OHsExportPublisher.ValidateBeatmapDestination(request.BeatmapPath, generated, token);
            progress?.Report(new(40, "Generating samples"));
            async Task BuildAsync(string stage, CancellationToken cancellation)
            {
                await OHsExportAudio.WriteAsync(request.ProjectPath, stage, plan, request.Options, cancellation, (path, _) => decoded[path].Retain(), progress, memoryBudget);
                await File.WriteAllTextAsync(Path.Combine(stage, difficultyName), OHsExportBeatmapWriter.Write(input, plan), sourceEncoding, cancellation);
                foreach (var source in fingerprints)
                {
                    cancellation.ThrowIfCancellationRequested(); ValidateSource(source.Key);
                    if (OHsExportPublisher.HashFile(source.Key) != source.Value) throw new IOException($"Export source changed during preparation: {source.Key}");
                }
                progress?.Report(new(95, "Validating export"));
            }
            var directory = request.Options.Destination == OHsExportDestination.Beatmap
                ? await publisher.PublishBeatmapAsync(request.BeatmapPath, generated, BuildAsync, token, progress)
                : await publisher.PublishAsync(request.ProjectPath, generated, BuildAsync, token, progress, request.Options.OverwriteEverything);
            progress?.Report(new(100, "Export complete"));
            return directory;
        }
        finally { foreach (var audio in decoded.Values) audio.Dispose(); }
    }, token);
}
