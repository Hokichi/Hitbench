using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Messaging;
using Soundie.Resources.DataModels;
using Soundie.Resources.Messages;
using Soundie.Services.Ui;

namespace Soundie.ViewModels.Popups;

public sealed partial class InstrumentalArrangementDrawerVM : ObservableObject
{
    private InstrumentalTrackSnapshot snapshot;
    private OHsInstrumentalTimelineSnapshot timeline;
    private readonly IMessenger messenger;
    private readonly IUiScheduler scheduler;
    private Task notifications = Task.CompletedTask;
    private bool closed, registered;
    private long saveVersion = -1, requestSequence;
    private Guid pendingRequest;
    public string? TrackColor => snapshot.TrackColor;
    public int TrackColorIndex => snapshot.TrackColorIndex;
    public IReadOnlyList<InstrumentalNoteSnapshot> Notes => snapshot.Notes;
    public IReadOnlyDictionary<string, string> KeyFiles => snapshot.KeyFiles;
    public IReadOnlyList<int> Offsets => timeline.Offsets;
    public IReadOnlyList<int> Bpms => timeline.Bpms;
    public TimeSpan Duration => timeline.Duration;
    public TimeSpan Position => timeline.Position;
    public bool IsPlaying => timeline.IsPlaying;
    public bool CanEdit => !closed && timeline.CanEdit;
    public int Subdivision => timeline.Subdivision;
    public int SelectedSubdivisionIndex => timeline.SelectedSubdivisionIndex;
    public double ZoomFactor => timeline.ZoomFactor;
    public bool ScrollWithMarker => timeline.ScrollWithMarker;
    public string? PlaybackWarning => timeline.PlaybackWarning;
    public string EditingHint => !CanEdit ? "Note editing requires a song with valid BPM timing data."
        : Notes.Count == 0 ? "No notes yet. Click the piano roll or press Enter to place a note." : "";
    [ObservableProperty] private Guid? selectedNoteId;
    [ObservableProperty] private string? errorMessage;
    [ObservableProperty][NotifyPropertyChangedFor(nameof(SaveStatus))] private bool isSaving;
    [ObservableProperty][NotifyPropertyChangedFor(nameof(SaveStatus))] private string? saveError;
    public string PopupTitle => string.Format(System.Globalization.CultureInfo.CurrentCulture, "Arrangement · {0}", string.IsNullOrWhiteSpace(snapshot.Name) ? "Instrument" : snapshot.Name);
    public string SaveStatus => saveVersion < 0 ? "Checking save status…" : SaveError is not null ? "Save failed" : IsSaving ? "Saving…" : "Autosaved";
    public string MappingStatus
    {
        get
        {
            var missing = Notes.Select(n => n.Key).Distinct().Where(k => !KeyFiles.TryGetValue(k, out var f) ||
             snapshot.AvailableFiles is not null && !snapshot.AvailableFiles.Contains(f, StringComparer.OrdinalIgnoreCase)).ToArray();
            return missing.Length == 0 ? "" : string.Format(System.Globalization.CultureInfo.CurrentCulture, "Unmapped or missing samples: {0}", string.Join(", ", missing));
        }
    }

    public InstrumentalArrangementDrawerVM(InstrumentalEditorContext context, IMessenger messenger, IUiScheduler scheduler)
    {
        snapshot = context.Track; timeline = context.Timeline;
        this.messenger = messenger; this.scheduler = scheduler;
    }
    private void Schedule(Action action)
    { notifications = ApplyAsync(notifications, action); }
    private async Task ApplyAsync(Task previous, Action action)
    {
        try { await previous; await scheduler.InvokeAsync(() => { if (!closed) action(); }, CancellationToken.None); }
        catch (Exception error) { System.Diagnostics.Trace.TraceError("Arrangement presentation failed: {0}", error); }
    }
    public Task InitializeAsync(CancellationToken token)
    {
        if (registered || closed || token.IsCancellationRequested) return Task.CompletedTask;
        registered = true;
        messenger.Register<OHsInstrumentalTimelineChangedMessage>(this, (_, m) => Schedule(() =>
        {
            if (m.Snapshot.Context != snapshot.Context || m.Snapshot.Revision < timeline.Revision) return;
            var old = timeline; timeline = m.Snapshot;
            if (old.Position != timeline.Position) OnPropertyChanged(nameof(Position));
            if (old.IsPlaying != timeline.IsPlaying) OnPropertyChanged(nameof(IsPlaying));
            if (old.Duration != timeline.Duration) OnPropertyChanged(nameof(Duration));
            if (!ReferenceEquals(old.Offsets, timeline.Offsets) && !old.Offsets.SequenceEqual(timeline.Offsets)) OnPropertyChanged(nameof(Offsets));
            if (!ReferenceEquals(old.Bpms, timeline.Bpms) && !old.Bpms.SequenceEqual(timeline.Bpms)) OnPropertyChanged(nameof(Bpms));
            if (old.Subdivision != timeline.Subdivision) OnPropertyChanged(nameof(Subdivision));
            if (old.SelectedSubdivisionIndex != timeline.SelectedSubdivisionIndex) OnPropertyChanged(nameof(SelectedSubdivisionIndex));
            if (old.ZoomFactor != timeline.ZoomFactor) OnPropertyChanged(nameof(ZoomFactor));
            if (old.ScrollWithMarker != timeline.ScrollWithMarker) OnPropertyChanged(nameof(ScrollWithMarker));
            if (old.PlaybackWarning != timeline.PlaybackWarning) OnPropertyChanged(nameof(PlaybackWarning));
            if (old.IsAdditionMode != timeline.IsAdditionMode)
            { OnPropertyChanged(nameof(IsAdditionMode)); OnPropertyChanged(nameof(IsSelectionMode)); }
            if (old.CanEdit != timeline.CanEdit)
            { OnPropertyChanged(nameof(CanEdit)); OnPropertyChanged(nameof(EditingHint)); }
            if (old.CanEdit != timeline.CanEdit || old.CanUseTransport != timeline.CanUseTransport || old.IsAdditionMode != timeline.IsAdditionMode)
                NotifyNoteCommands();
        }));
        messenger.Register<InstrumentalTrackChangedMessage>(this, (_, m) => Schedule(() =>
        {
            if (m.Snapshot.Context != snapshot.Context || m.Snapshot.Revision < snapshot.Revision) return;
            snapshot = m.Snapshot;
            foreach (var name in new[] { nameof(TrackColor), nameof(TrackColorIndex), nameof(Notes), nameof(KeyFiles), nameof(PopupTitle), nameof(MappingStatus), nameof(EditingHint) }) OnPropertyChanged(name);
            SetNoteSelection(SelectedNoteIds, SelectedNoteId);
        }));
        messenger.Register<InstrumentalEditResultMessage>(this, (_, m) => Schedule(() =>
        {
            if (m.Context != snapshot.Context || m.RequestId != pendingRequest) return;
            ErrorMessage = m.Error;
            if (m.Error is null) SetNoteSelection(m.SelectedNoteIds ?? (m.SelectedNoteId is {} id ? new[] { id } : Array.Empty<Guid>()), m.SelectedNoteId);
        }));
        messenger.Register<InstrumentalSaveStateChangedMessage>(this, (_, m) => Schedule(() =>
        {
            if (m.Context != snapshot.Context || m.Version < saveVersion) return;
            saveVersion = m.Version; IsSaving = m.IsSaving; SaveError = m.Error; OnPropertyChanged(nameof(SaveStatus));
        }));
        messenger.Send(new InstrumentalEditorReadyMessage(snapshot.Context));
        return notifications;
    }
    public void RequestZoom(double value) { if (CanEdit && double.IsFinite(value)) messenger.Send(new OHsInstrumentalTimelineEditRequestedMessage(snapshot.Context, ++requestSequence, ZoomFactor: Math.Clamp(value, 8, 128))); }
    public void RequestSubdivision(int value) { if (CanEdit) messenger.Send(new OHsInstrumentalTimelineEditRequestedMessage(snapshot.Context, ++requestSequence, SelectedSubdivisionIndex: Math.Clamp(value, 0, 8))); }
    public void RequestFollow(bool value) { if (!closed && ScrollWithMarker != value) messenger.Send(new OHsInstrumentalTimelineEditRequestedMessage(snapshot.Context, ++requestSequence, ScrollWithMarker: value)); }
    public void SelectNote(Guid? id) => SetNoteSelection(id is {} value ? new[] { value } : Array.Empty<Guid>(), id);
    public void PlaceNote(string key, double ms) { if (!CanEdit) return; pendingRequest = Guid.NewGuid(); messenger.Send(new PlaceInstrumentalNoteRequestedMessage(snapshot.Context, pendingRequest, key, ms, Subdivision)); }
    public void MoveNote(Guid id, string key, double ms) { if (!CanEdit) return; pendingRequest = Guid.NewGuid(); messenger.Send(new MoveInstrumentalNoteRequestedMessage(snapshot.Context, pendingRequest, id, key, ms, Subdivision)); }
    public void RemoveNote(Guid id) { if (!CanEdit) return; pendingRequest = Guid.NewGuid(); messenger.Send(new RemoveInstrumentalNoteRequestedMessage(snapshot.Context, pendingRequest, id)); }

    public async Task DeactivateAsync()
    {
        if (!closed) { closed = true; messenger.UnregisterAll(this); OnPropertyChanged(nameof(CanEdit)); NotifyNoteCommands(); }
        await notifications;
    }
}
