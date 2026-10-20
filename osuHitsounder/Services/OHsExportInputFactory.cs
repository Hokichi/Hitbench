using System.Globalization;
using System.IO;
using osuHitsounder.Core.Constants;
using osuHitsounder.Core.Entities;
using osuHitsounder.Resources.DataModels;
using osuHitsounder.Services.FileSystem;
namespace osuHitsounder.Services;

public static class OHsExportInputFactory
{
    public static OHsExportInput Create(ProjectData project, OHsExportBeatmap beatmap, OHsExportOptions options, CancellationToken token)
    {
        options.Validate(); token.ThrowIfCancellationRequested();
        var controls = project.ControlPoints.GroupBy(p => p.Timestamp).Select(g => g.Last()).Where(p => !p.IsDisabled).OrderBy(p => p.Timestamp).ToArray();
        foreach (var point in controls)
            if (point.Timestamp < 0 || point.Volume is < 0 or > 100 || !ControlPoint.SamplesetOptions.Contains(point.Sampleset))
                throw new InvalidDataException("Invalid export control point.");
        (int Set, int Volume) State(decimal time)
        {
            var p = controls.LastOrDefault(p => p.Timestamp <= time);
            return p is null ? (1, 100) : (Array.IndexOf(ControlPoint.SamplesetOptions.ToArray(), p.Sampleset) + 1, p.Volume);
        }
        var required = new List<OHsExportTimingPoint>();
        var sv = -100m; var effects = 0;
        foreach (var source in beatmap.TimingPoints)
        {
            token.ThrowIfCancellationRequested();
            var value = decimal.Parse(source.BeatLength, CultureInfo.InvariantCulture);
            var meaningful = source.Kind == OHsExportTimingKind.SourceRed || value != sv || source.Effects != effects;
            sv = source.Kind == OHsExportTimingKind.SourceRed ? -100 : value; effects = source.Effects;
            if (!meaningful) continue;
            var state = State(source.Timestamp);
            required.Add(source with { SampleSet = state.Set, Volume = state.Volume, SampleIndex = options.StartingIndex });
        }
        foreach (var point in controls)
        {
            var source = beatmap.TimingPoints.LastOrDefault(p => p.Timestamp <= point.Timestamp);
            var length = source is null || source.Kind == OHsExportTimingKind.SourceRed ? "-100" : source.BeatLength;
            required.RemoveAll(p => p.Timestamp == point.Timestamp && p.Kind == OHsExportTimingKind.SourceState);
            var state = State(point.Timestamp);
            required.Add(new(point.Timestamp, length, source?.Meter ?? 4, state.Set, options.StartingIndex, point.Volume, source?.Effects ?? 0, OHsExportTimingKind.ProjectControl));
        }
        var voices = new List<OHsExportVoice>();
        var solo = project.Tracks.Any(t => t.IsTrackSoloed);
        for (var order = 0; order < project.Tracks.Count; order++)
        {
            token.ThrowIfCancellationRequested(); var track = project.Tracks[order];
            if (track.IsTrackMuted || solo && !track.IsTrackSoloed) continue;
            if (track.Volume is < 0 or > 100 || !Enum.IsDefined(track.SampleType) || !Track.AdditionOptions.Contains(track.Addition))
                throw new InvalidDataException("Invalid export track.");
            void Add(int time, string file)
            {
                if (time < 0 || SamplePaths.GetRelativePathError(file) is not null) throw new InvalidDataException("Invalid export sample.");
                var set = track.Addition == "None" ? ControlPoint.SamplesetOptions[State((decimal)time + 5).Set - 1] : track.Addition;
                voices.Add(new(time, file.Replace('\\', '/'), track.Volume, set, track.SampleType, order));
            }
            if (track is InstrumentalTrack instrument)
                foreach (var note in instrument.Notes)
                {
                    token.ThrowIfCancellationRequested();
                    if (!InstrumentalKeys.TryGetIndex(note.Key, out _) || !instrument.KeyFiles.TryGetValue(note.Key, out var file)) throw new InvalidDataException("Instrumental note has no valid sample mapping.");
                    Add(note.Timestamp, file);
                }
            else foreach (var sample in track.Samples) { token.ThrowIfCancellationRequested(); Add(sample.Timestamp, sample.FileName); }
        }
        return new(options, beatmap, voices, required.OrderBy(p => p.Timestamp).ThenBy(p => p.Kind).ToArray());
    }
}
