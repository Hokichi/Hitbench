using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using osuHitsounder.Core.Entities;
using osuHitsounder.Services;
using osuHitsounder.Services.FileSystem;
using osuHitsounder.Services.Ui;
using osuHitsounder.Services.Dialogs;
using osuHitsounder.Services.ProjectFiles;
using osuHitsounder.Resources.Messages;
using CommunityToolkit.Mvvm.Messaging;
using System.Collections.ObjectModel;
using System.IO;

using osuHitsounder.Core.Constants;
using osuHitsounder.Resources.DataModels;

namespace osuHitsounder.ViewModels.Shell;

public partial class WorkstationPageVM
{
    private bool CanResnapAllSamples() => CanPlaceSamples && (Project!.ControlPoints.Count == 0 || CanEditControlPoints) && (Project.ControlPoints.Count > 0 || tracks.Any(track => track.Samples.Count > 0 || track is InstrumentalTrack { Notes.Count: > 0 }));

    // Legacy public name retained for callers; the workstation action includes instrumental notes.
    public bool ResnapAllSampleNodes()
    {
        if (!CanResnapAllSamples()) return false;
        var timing = BeatGridCalculator.PrepareTiming(Project!.Offsets, Project.BPMs);
        if (timing is null) { ErrorMessage = "No usable grid beat for resnapping samples."; return false; }
        var entries = new List<NodeHistoryEntry>();
        foreach (var track in tracks.Where(CanEditTrackGrid))
        for (var index = 0; index < track.Samples.Count; index++)
        {
            var sample = track.Samples[index];
            var timestamp = BeatGridCalculator.GetNearestTimestamp(timing.AsSpan(), Subdivision,
                sample.Timestamp, Waveform!.Duration.TotalMilliseconds);
            if (timestamp is null)
            {
                ErrorMessage = "No usable grid beat for resnapping samples.";
                return false;
            }
            if (timestamp.Value != sample.Timestamp)
                entries.Add(new(track, sample, index, NodeHistoryOperation.Movement, sample.Timestamp, timestamp.Value));
        }
        var instruments = new List<(InstrumentalTrack Track, IReadOnlyList<InstrumentalNoteSnapshot> Before, IReadOnlyList<InstrumentalNoteSnapshot> After)>();
        foreach (var track in tracks.OfType<InstrumentalTrack>())
        {
            var before = NoteSnapshots(track);
            var after = ResnapInstrumentNotes(before, timing);
            if (after is null) { ErrorMessage = "No usable grid beat for resnapping notes."; return false; }
            if (!before.SequenceEqual(after)) instruments.Add((track, before, after));
        }
        var points = new List<(ControlPoint Point, int Timestamp)>();
        var occupied = Project.ControlPoints.GroupBy(p => p.Timestamp).ToDictionary(g => g.Key, g => g.Count());
        foreach (var point in Project.ControlPoints.OrderBy(p => p.Timestamp))
        {
            var target = BeatGridCalculator.GetNearestTimestamp(timing.AsSpan(), Subdivision, point.Timestamp, Waveform!.Duration.TotalMilliseconds);
            if (target is null) { ErrorMessage = "No usable grid beat for resnapping control points."; return false; }
            if (target == point.Timestamp || occupied.GetValueOrDefault(target.Value) > 0) continue;
            occupied[point.Timestamp]--;
            occupied[target.Value] = 1;
            points.Add((point, target.Value));
        }
        CancelNodeDrag();
        ErrorMessage = null;
        if (entries.Count == 0 && instruments.Count == 0 && points.Count == 0) return false;
        foreach (var entry in entries) entry.Sample.Timestamp = entry.NextTimestamp;
        if (entries.Count > 0) history.RecordBatch(entries);
        foreach (var item in instruments)
        {
            TryApplyInstrumentNotes(item.Track, item.After);
            InstrumentHistory(item.Track).Record(item.Before, item.After,
                item.After.Where((note, index) => note.Timestamp != item.Before[index].Timestamp).Select(note => note.Id).ToArray());
        }
        if (instruments.Count > 0) { instrumentRevision++; PublishInstrumentSnapshot(); }
        foreach (var item in points) item.Point.Timestamp = item.Timestamp;
        if (points.Count > 0) FinishControlPointMutation(true, entries.Count == 0 && instruments.Count == 0);
        if (entries.Count > 0 || instruments.Count > 0) PublishNodeChange();
        return true;
    }

    private IReadOnlyList<InstrumentalNoteSnapshot>? ResnapInstrumentNotes(IReadOnlyList<InstrumentalNoteSnapshot> notes, (int Offset, int Bpm)[] timing)
    {
        var occupied = notes.Select(note => (note.Key, note.Timestamp)).ToHashSet();
        var result = notes.ToArray();
        for (var index = 0; index < notes.Count; index++)
        {
            var note = notes[index];
            var target = BeatGridCalculator.GetNearestTimestamp(timing.AsSpan(), Subdivision,
                note.Timestamp, Waveform!.Duration.TotalMilliseconds);
            if (target is null) return null;
            if (target == note.Timestamp || occupied.Contains((note.Key, target.Value))) continue;
            occupied.Remove((note.Key, note.Timestamp));
            occupied.Add((note.Key, target.Value));
            result[index] = note with { Timestamp = target.Value };
        }
        return result;
    }

    [RelayCommand(CanExecute = nameof(CanResnapAllSamples))]
    private void ResnapAllSamples() => ResnapAllSampleNodes();

}
