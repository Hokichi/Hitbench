using Hitbench.Core.Entities;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Hitbench.Resources.DataModels;
using Hitbench.Resources.Messages;
using Hitbench.Services.Ui;

namespace Hitbench.ViewModels.Popups;

public sealed partial class InstrumentalArrangementDrawerVM : ObservableObject
{
    private InstrumentalTrackSnapshot snapshot;
    private OHsInstrumentalTimelineSnapshot timeline;
    private readonly IMessenger messenger;
    private readonly IUiScheduler scheduler;
    private Task notifications = Task.CompletedTask;
    private bool closed, registered;
    public bool IsKeysMappingRequested { get; private set; }
    private bool CanRequestKeysMapping() => registered && !closed && !IsKeysMappingRequested;
    [RelayCommand(CanExecute = nameof(CanRequestKeysMapping))]
    private void RequestKeysMapping()
    {
        if (!CanRequestKeysMapping()) return;
        messenger.Send(new OHsInstrumentalKeysMappingRequestedMessage(snapshot.Context));
    }
    private long saveVersion = -1, requestSequence;
    private Guid pendingRequest;
    public string? TrackColor => snapshot.TrackColor;
    public int TrackColorIndex => snapshot.TrackColorIndex;
    public IReadOnlyList<InstrumentalNoteSnapshot> Notes => snapshot.Notes;
    public IReadOnlyDictionary<string, string> KeyFiles => snapshot.KeyFiles;
    public IReadOnlyList<UninheritedPoint> UninheritedPoints => timeline.UninheritedPoints;
    public TimeSpan Duration => timeline.Duration;
    public double DisplayStartMilliseconds => Math.Min(0, Notes.Count == 0 ? 0 : Notes.Min(n => n.Timestamp));
    public double DisplayEndMilliseconds => Math.Max(Duration.TotalMilliseconds, Notes.Count == 0 ? 0 : Notes.Max(n => n.Timestamp));
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
        RequestKeysMappingCommand.NotifyCanExecuteChanged();
        messenger.Register<OHsInstrumentalKeysMappingAcceptedMessage>(this, (_, m) => Schedule(() =>
        {
            if (m.Context != snapshot.Context || IsKeysMappingRequested) return;
            IsKeysMappingRequested = true;
            RequestKeysMappingCommand.NotifyCanExecuteChanged();
            OnPropertyChanged(nameof(IsKeysMappingRequested));
        }));
        messenger.Register<OHsInstrumentalTimelineChangedMessage>(this, (_, m) => Schedule(() =>
        {
            if (m.Snapshot.Context != snapshot.Context || m.Snapshot.Revision < timeline.Revision) return;
            var old = timeline; timeline = m.Snapshot;
            if (old.Position != timeline.Position) OnPropertyChanged(nameof(Position));
            if (old.IsPlaying != timeline.IsPlaying) OnPropertyChanged(nameof(IsPlaying));
            if (old.Duration != timeline.Duration) { OnPropertyChanged(nameof(Duration)); OnPropertyChanged(nameof(DisplayEndMilliseconds)); SetNoteSelection(SelectedNoteIds, SelectedNoteId); }
            if (!ReferenceEquals(old.UninheritedPoints, timeline.UninheritedPoints) &&
                !old.UninheritedPoints.Select(p => (p.Offset, p.BPM)).SequenceEqual(timeline.UninheritedPoints.Select(p => (p.Offset, p.BPM))))
                OnPropertyChanged(nameof(UninheritedPoints));
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
            foreach (var name in new[] { nameof(TrackColor), nameof(TrackColorIndex), nameof(Notes), nameof(KeyFiles), nameof(PopupTitle), nameof(MappingStatus), nameof(EditingHint), nameof(DisplayStartMilliseconds), nameof(DisplayEndMilliseconds) }) OnPropertyChanged(name);
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
    public void MoveNote(Guid id, string key, double ms) { if (!CanEdit || !Notes.Any(n => n.Id == id && IsNoteEditable(n))) return; pendingRequest = Guid.NewGuid(); messenger.Send(new MoveInstrumentalNoteRequestedMessage(snapshot.Context, pendingRequest, id, key, ms, Subdivision)); }
    public void RemoveNote(Guid id) { if (!CanEdit || !Notes.Any(n => n.Id == id && IsNoteEditable(n))) return; pendingRequest = Guid.NewGuid(); messenger.Send(new RemoveInstrumentalNoteRequestedMessage(snapshot.Context, pendingRequest, id)); }

    public async Task DeactivateAsync()
    {
        if (!closed) { closed = true; messenger.UnregisterAll(this); OnPropertyChanged(nameof(CanEdit)); NotifyNoteCommands(); RequestKeysMappingCommand.NotifyCanExecuteChanged(); }
        await notifications;
    }
}
