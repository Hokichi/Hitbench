using System.Globalization;
using System.IO;
using Onset.Core.Constants;
using Onset.Core.Entities;
using Onset.Resources.DataModels;
using Onset.Services.FileSystem;
namespace Onset.Services;

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
            var low = 0; var high = controls.Length;
            while (low < high)
            {
                var middle = low + (high - low) / 2;
                if (controls[middle].Timestamp <= time) low = middle + 1; else high = middle;
            }
            if (low == 0) return (1, 100);
            var p = controls[low - 1];
            return (p.Sampleset switch { "Normal" => 1, "Soft" => 2, "Drum" => 3, _ => throw new InvalidDataException("Invalid sampleset.") }, p.Volume);
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
        var sourceIndex = 0;
        foreach (var point in controls)
        {
            while (sourceIndex < beatmap.TimingPoints.Count && beatmap.TimingPoints[sourceIndex].Timestamp <= point.Timestamp) sourceIndex++;
            var source = sourceIndex == 0 ? null : beatmap.TimingPoints[sourceIndex - 1];
            var length = source is null || source.Kind == OHsExportTimingKind.SourceRed ? "-100" : source.BeatLength;
            var state = State(point.Timestamp);
            required.Add(new(point.Timestamp, length, source?.Meter ?? 4, state.Set, options.StartingIndex, point.Volume, source?.Effects ?? 0, OHsExportTimingKind.ProjectControl));
        }
        var controlTimes = controls.Select(p => (decimal)p.Timestamp).ToHashSet();
        required.RemoveAll(p => p.Kind == OHsExportTimingKind.SourceState && controlTimes.Contains(p.Timestamp));
        var voices = new List<OHsExportVoice>();
        var solo = project.Tracks.Any(t => t.IsTrackSoloed);
        for (var order = 0; order < project.Tracks.Count; order++)
        {
            token.ThrowIfCancellationRequested(); var track = project.Tracks[order];
            if (track.IsTrackMuted || solo && !track.IsTrackSoloed) continue;
            if (track.Volume is < 0 or > 200 || !Enum.IsDefined(track.SampleType) || !Track.AdditionOptions.Contains(track.Addition))
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
