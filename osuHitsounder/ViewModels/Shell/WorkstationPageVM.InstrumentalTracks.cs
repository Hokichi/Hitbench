using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using osuHitsounder.Core.Constants;
using osuHitsounder.Core.Entities;
using osuHitsounder.Resources.DataModels;
using osuHitsounder.Resources.Messages;
using osuHitsounder.Services;
using osuHitsounder.Services.FileSystem;
using System.IO;

namespace osuHitsounder.ViewModels.Shell;

public partial class WorkstationPageVM
{
    private readonly Dictionary<InstrumentalTrack, InstrumentalNoteHistoryManager> instrumentHistories = [];
    private InstrumentalNoteHistoryManager InstrumentHistory(InstrumentalTrack track)
    { if (!instrumentHistories.TryGetValue(track, out var value)) instrumentHistories[track] = value = new(); return value; }
    private readonly Dictionary<InstrumentalTrack, Guid> instrumentIds = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<InstrumentalNote, Guid> instrumentNoteIds = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<Guid, long> instrumentEventIds = [];
    private long InstrumentEventId(Guid id)
    { if (!instrumentEventIds.TryGetValue(id, out var value)) instrumentEventIds[id] = value = nextSampleEventId++; return value; }
    private InstrumentalTrackContext? activeInstrumentContext;
    private CancellationTokenSource? instrumentCancellation;
    private Task? instrumentWorkflow;
    private bool instrumentBusy;
    private string? instrumentAudioPath;
    private long instrumentRevision, saveStateVersion;
    private int pendingProjectWrites;
    public bool CanAddInstrumentalTrack => CanEditTracks && !instrumentBusy;
    public bool CanOpenInstrumentalEditor => !controlPointsBusy && CanPlaceSamples && !instrumentBusy && instrumentDialogs is not null && messenger is not null;
    private Guid InstrumentId(InstrumentalTrack track)
    { if (!instrumentIds.TryGetValue(track, out var id)) instrumentIds[track] = id = Guid.NewGuid(); return id; }
    private Guid InstrumentNoteId(InstrumentalNote note)
    { if (!instrumentNoteIds.TryGetValue(note, out var id)) instrumentNoteIds[note] = id = Guid.NewGuid(); return id; }
    private InstrumentalTrack? ActiveInstrument(InstrumentalTrackContext context) =>
        activeInstrumentContext == context && Project is not null && context.ProjectSessionId == sampleSession
        ? tracks.OfType<InstrumentalTrack>().FirstOrDefault(t => InstrumentId(t) == context.TrackId) : null;
    private InstrumentalNoteSnapshot[] NoteSnapshots(InstrumentalTrack track) =>
        track.Notes.Select(n => new InstrumentalNoteSnapshot(InstrumentNoteId(n), n.Key, n.Timestamp, n.Volume)).ToArray();
    private InstrumentalTrackSnapshot InstrumentSnapshot(InstrumentalTrack track, InstrumentalTrackContext context) =>
        new(context, track.Name, track.Volume, track.KeyFiles, NoteSnapshots(track), sampleFiles is null ? null : availableSamples.ToArray(), instrumentRevision, track.FolderName,
            tracks.OfType<InstrumentalTrack>().Where(t => t != track && t.FolderName.Length > 0).Select(t => t.FolderName).ToArray(), InstrumentHistory(track).CanUndo, InstrumentHistory(track).CanRedo);
    private void PublishInstrumentSnapshot()
    {
        if (activeInstrumentContext is { } context && ActiveInstrument(context) is { } track)
            messenger?.Send(new InstrumentalTrackChangedMessage(InstrumentSnapshot(track, context)));
    }
    private void PublishInstrumentSaveState()
    {
        saveStateVersion++;
        if (activeInstrumentContext is { } context)
            messenger?.Send(new InstrumentalSaveStateChangedMessage(context, saveStateVersion, pendingProjectWrites > 0, SaveError));
    }
    private void RegisterInstrumentMessages()
    {
        if (messenger is null) return;
        messenger.Register<ReplaceInstrumentalNotesRequestedMessage>(this, (_, m) =>
        {
            if (ActiveInstrument(m.Context) is not { } track) return;
            if (m.ExpectedRevision != instrumentRevision) { InstrumentResult(m.Context, m.RequestId, false, null, "Notes changed. Retry the edit."); return; }
            CommitInstrumentNotes(track, m.Context, m.RequestId, m.DesiredNotes, m.AffectedNoteIds);
        });
        messenger.Register<ReplayInstrumentalNoteHistoryRequestedMessage>(this, (_, m) =>
        {
            if (ActiveInstrument(m.Context) is not { } track) return;
            var history = InstrumentHistory(track); IReadOnlyList<Guid> selected;
            var changed = m.Redo ? history.Redo(NoteSnapshots(track), notes => TryApplyInstrumentNotes(track, notes), out selected)
                : history.Undo(NoteSnapshots(track), notes => TryApplyInstrumentNotes(track, notes), out selected);
            PublishInstrumentTransaction(m.Context, m.RequestId, changed, selected, changed ? null : "No valid note history to replay.");
        });
        messenger.Register<PlaceInstrumentalNoteRequestedMessage>(this, (_, m) => PlaceInstrumentNote(m));
        messenger.Register<MoveInstrumentalNoteRequestedMessage>(this, (_, m) => MoveInstrumentNote(m));
        messenger.Register<RemoveInstrumentalNoteRequestedMessage>(this, (_, m) => RemoveInstrumentNote(m));
        messenger.Register<InstrumentalEditorReadyMessage>(this, (_, m) =>
        { if (ActiveInstrument(m.Context) is not null) { PublishInstrumentSnapshot(); PublishInstrumentSaveState(); } });
    }
    private bool CanViewArrangement(InstrumentalTrack? track) =>
        CanOpenInstrumentalEditor && track is not null && tracks.Contains(track) && track.KeyFiles.Count > 0;
    [RelayCommand(CanExecute = nameof(CanViewArrangement))]
    private Task ViewArrangementAsync(InstrumentalTrack track) =>
        CanViewArrangement(track) ? OpenInstrumentAsync(track, true) : Task.CompletedTask;
    [RelayCommand(CanExecute = nameof(CanOpenInstrumentalEditor))]
    private Task KeysMappingAsync(InstrumentalTrack track) => OpenInstrumentAsync(track, false);
    private async Task OpenInstrumentAsync(InstrumentalTrack track, bool arrangement)
    {
        await SettleInstrumentalFoldersAsync();
        if (!CanOpenInstrumentalEditor || !tracks.Contains(track) || projectPath is null) return;
        instrumentBusy = true;
        instrumentCancellation?.Dispose();
        instrumentCancellation = CancellationTokenSource.CreateLinkedTokenSource(loadCancellation?.Token ?? default);
        activeInstrumentContext = new(projectPath, Project!.BeatmapPath, sampleSession, Guid.NewGuid(), InstrumentId(track));
        NotifyInstrumentState();
        await (instrumentWorkflow = RunInstrumentWorkflowAsync(track, arrangement, activeInstrumentContext, instrumentCancellation.Token));
    }
    private async Task RunInstrumentWorkflowAsync(InstrumentalTrack track, bool arrangement, InstrumentalTrackContext context, CancellationToken token)
    {
        try
        {
            if (arrangement)
            {
                pendingPlayCancellation?.Cancel();
                await playback.PauseAsync(token); RefreshPlayback();
                await instrumentDialogs!.ShowArrangementAsync(new(InstrumentSnapshot(track, context), instrumentAudioPath!, Waveform!,
                    Project!.Offsets, Project.BPMs, Position, Subdivision, ZoomFactor, SongVolume, SamplesVolume, ApplicationVolume,
                    Project.ControlPoints.Select(p => new ControlPointVolumeSnapshot(p.Timestamp, p.Volume)).ToArray()), token);
            }
            else await instrumentDialogs!.ShowKeysMappingAsync(InstrumentSnapshot(track, context), token);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception error) { ErrorMessage = error.Message; }
        finally
        {
            try { await DrainProjectSaveQueueAsync(); } catch (Exception error) { SaveError = error.Message; }
            activeInstrumentContext = null; instrumentBusy = false; NotifyInstrumentState();
        }
    }
    public async Task SettleInstrumentalEditorAsync()
    { instrumentCancellation?.Cancel(); if (instrumentWorkflow is { } task) await task; instrumentWorkflow = null; instrumentCancellation?.Dispose(); instrumentCancellation = null; }
    private async Task DrainProjectSaveQueueAsync()
    { await sampleChangeNotifications; await saveTail; if (lastSaveError is not null) throw new IOException("Could not save project.", lastSaveError); }
    private void NotifyInstrumentState()
    {
        OnPropertyChanged(nameof(CanAddInstrumentalTrack)); OnPropertyChanged(nameof(CanOpenInstrumentalEditor)); OnPropertyChanged(nameof(HasPendingSaves));
        AddPianoTrackCommand.NotifyCanExecuteChanged(); ViewArrangementCommand.NotifyCanExecuteChanged(); KeysMappingCommand.NotifyCanExecuteChanged();
        NotifyEditingState();
        NotifyCommandState();
    }
    private int? InstrumentTimestamp(double requested, int subdivision, bool moving)
    {
        if (Waveform is null || Project is null || !double.IsFinite(requested) || requested < 0 || requested >= Waveform.Duration.TotalMilliseconds) return null;
        var time = moving ? BeatGridCalculator.GetNearestTimestamp(Project.Offsets, Project.BPMs, subdivision, requested, Waveform.Duration.TotalMilliseconds)
            : BeatGridCalculator.GetContainingInterval(Project.Offsets, Project.BPMs, subdivision, requested, Waveform.Duration.TotalMilliseconds) is { } interval
                ? (int?)Math.Round(interval.StartMilliseconds, MidpointRounding.AwayFromZero) : null;
        return time is >= 0 && time < Waveform.Duration.TotalMilliseconds ? time : null;
    }
    private void InstrumentResult(InstrumentalTrackContext context, Guid request, bool changed, Guid? selected, string? error = null)
    {
        if (changed) { instrumentRevision++; QueueSave(); QueuePlaybackUpdate(); PublishInstrumentSnapshot(); }
        messenger?.Send(new InstrumentalEditResultMessage(context, request, changed, selected, error));
    }
    private bool TryApplyInstrumentNotes(InstrumentalTrack track, IReadOnlyList<InstrumentalNoteSnapshot> desired)
    {
        if (Waveform is null || desired.Any(n => n.Id == Guid.Empty || n.Timestamp < 0 || n.Timestamp >= Waveform.Duration.TotalMilliseconds || n.Volume is < 0 or > 100 || !InstrumentalKeys.TryGetIndex(n.Key, out _)) || desired.Select(n => n.Id).Distinct().Count() != desired.Count) return false;
        var replacement = desired.Select(n => new InstrumentalNote { Key = n.Key, Timestamp = n.Timestamp, Volume = n.Volume }).ToArray();
        var old = track.Notes.ToArray();
        if (!track.TryReplaceNotes(replacement)) return false;
        foreach (var note in old) { var id = InstrumentNoteId(note); instrumentNoteIds.Remove(note); if (!desired.Any(n => n.Id == id)) instrumentEventIds.Remove(id); }
        for (var i = 0; i < replacement.Length; i++) instrumentNoteIds[replacement[i]] = desired[i].Id;
        return true;
    }
    private void CommitInstrumentNotes(InstrumentalTrack track, InstrumentalTrackContext context, Guid request, IReadOnlyList<InstrumentalNoteSnapshot> desired, IReadOnlyList<Guid> affected)
    {
        var before = NoteSnapshots(track);
        if (before.SequenceEqual(desired)) { PublishInstrumentTransaction(context, request, false, affected, null); return; }
        if (!TryApplyInstrumentNotes(track, desired)) { PublishInstrumentTransaction(context, request, false, [], "Choose valid notes inside the song without overlapping key/time pairs."); return; }
        InstrumentHistory(track).Record(before, desired, affected);
        PublishInstrumentTransaction(context, request, true, affected, null);
    }
    private void PublishInstrumentTransaction(InstrumentalTrackContext context, Guid request, bool changed, IReadOnlyList<Guid> affected, string? error)
    {
        if (changed) { instrumentRevision++; QueueSave(); QueuePlaybackUpdate(); PublishInstrumentSnapshot(); }
        var ids = ActiveInstrument(context) is { } track ? affected.Where(id => track.Notes.Any(n => InstrumentNoteId(n) == id)).ToArray() : [];
        messenger?.Send(new InstrumentalEditResultMessage(context, request, changed, ids.Length > 0 ? ids[0] : null, error) { SelectedNoteIds = Array.AsReadOnly(ids) });
    }
    private void PlaceInstrumentNote(PlaceInstrumentalNoteRequestedMessage m)
    {
        if (ActiveInstrument(m.Context) is not { } track) return;
        var time = InstrumentTimestamp(m.RequestedMilliseconds, m.Subdivision, false);
        if (time is null || !InstrumentalKeys.TryGetIndex(m.Key, out _))
        { InstrumentResult(m.Context, m.RequestId, false, null, "Choose a valid key and time inside the song."); return; }
        var existing = track.Notes.FirstOrDefault(n => n.Key == m.Key && n.Timestamp == time);
        if (existing is not null) { InstrumentResult(m.Context, m.RequestId, false, InstrumentNoteId(existing)); return; }
        var id = Guid.NewGuid();
        CommitInstrumentNotes(track, m.Context, m.RequestId, [.. NoteSnapshots(track), new(id, m.Key, time.Value, 100)], [id]);
    }
    private void MoveInstrumentNote(MoveInstrumentalNoteRequestedMessage m)
    {
        if (ActiveInstrument(m.Context) is not { } track) return;
        var note = track.Notes.FirstOrDefault(n => InstrumentNoteId(n) == m.NoteId);
        var time = InstrumentTimestamp(m.RequestedMilliseconds, m.Subdivision, true);
        if (note is null || time is null || !InstrumentalKeys.TryGetIndex(m.Key, out _))
        { InstrumentResult(m.Context, m.RequestId, false, m.NoteId, "Choose a valid key and time inside the song."); return; }
        if (note.Key == m.Key && note.Timestamp == time) { InstrumentResult(m.Context, m.RequestId, false, m.NoteId); return; }
        CommitInstrumentNotes(track, m.Context, m.RequestId, NoteSnapshots(track).Select(n => n.Id == m.NoteId ? n with { Key = m.Key, Timestamp = time.Value } : n).ToArray(), [m.NoteId]);
    }
    private void RemoveInstrumentNote(RemoveInstrumentalNoteRequestedMessage m)
    {
        if (ActiveInstrument(m.Context) is not { } track) return;
        var note = track.Notes.FirstOrDefault(n => InstrumentNoteId(n) == m.NoteId);
        if (note is null) { InstrumentResult(m.Context, m.RequestId, false, null, "Note no longer exists."); return; }
        CommitInstrumentNotes(track, m.Context, m.RequestId, NoteSnapshots(track).Where(n => n.Id != m.NoteId).ToArray(), [m.NoteId]);
    }

}
