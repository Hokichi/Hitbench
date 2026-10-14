using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using osuHitsounder.Core.Entities;
using osuHitsounder.Resources.DataModels;
using osuHitsounder.Resources.Messages;
using osuHitsounder.Services;
using osuHitsounder.Services.Ui;

namespace osuHitsounder.ViewModels.Popups;

public sealed partial class InstrumentalArrangementPopupVM : ObservableObject
{
    private readonly InstrumentalEditorContext context;
    private InstrumentalTrackSnapshot snapshot;
    private readonly MarkerPlaybackCoordinator playback;
    private readonly IMessenger messenger;
    private readonly IUiScheduler scheduler;
    private readonly CancellationTokenSource lifetime = new();
    private readonly Dictionary<Guid, long> eventIds = [];
    private CancellationTokenSource? preparationCancellation;
    private CancellationTokenRegistration externalCancellation;
    private Task preparation = Task.CompletedTask, notifications = Task.CompletedTask;
    private Task? deactivation;
    private IReadOnlyList<SamplePlaybackEvent>? requestedEvents;
    private long nextEventId = 1, saveVersion = -1;
    private int prepareGeneration, subdivisionIndex = 3;
    private double zoomFactor = 32;
    private Guid pendingRequest;
    private bool configured, closed, registered, preparing;
    public IReadOnlyList<InstrumentalNoteSnapshot> Notes => snapshot.Notes;
    public IReadOnlyDictionary<string, string> KeyFiles => snapshot.KeyFiles;
    public IReadOnlyList<int> Offsets => context.Offsets;
    public IReadOnlyList<int> Bpms => context.Bpms;
    public WaveformData Waveform => context.Waveform;
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
    public bool CanEdit => !closed && Waveform.Duration > TimeSpan.Zero && new PlaybackMarkerClock(Offsets, Bpms).IsValid;
    public string EditingHint => !CanEdit ? "Note editing requires a song with valid BPM timing data."
        : Notes.Count == 0 ? "No notes yet. Click the piano roll or press Enter to place a note." : "";
    public bool CanPlay => CanEdit && configured && !preparing && playback.Audio.IsPrepared;
    public int SelectedSubdivisionIndex { get => subdivisionIndex; set { if (SetProperty(ref subdivisionIndex, Math.Clamp(value, 0, 8))) OnPropertyChanged(nameof(Subdivision)); } }
    public int Subdivision => new[] { 1, 2, 3, 4, 5, 6, 8, 12, 16 }[SelectedSubdivisionIndex];
    public double ZoomFactor { get => zoomFactor; set { if (double.IsFinite(value)) SetProperty(ref zoomFactor, Math.Clamp(value, 8, 128)); } }
    [ObservableProperty] private Guid? selectedNoteId;
    [ObservableProperty][NotifyPropertyChangedFor(nameof(ElapsedText))] private TimeSpan position;
    [ObservableProperty][NotifyPropertyChangedFor(nameof(PlayText))] private bool isPlaying;
    [ObservableProperty] private string? errorMessage;
    [ObservableProperty] private string? playbackWarning;
    [ObservableProperty][NotifyPropertyChangedFor(nameof(SaveStatus))] private bool isSaving;
    [ObservableProperty][NotifyPropertyChangedFor(nameof(SaveStatus))] private string? saveError;
    public string PlayText => IsPlaying ? "Pause" : "Play";
    public string ElapsedText => Position.ToString(@"mm\:ss\.fff", System.Globalization.CultureInfo.CurrentCulture);
    public InstrumentalArrangementPopupVM(InstrumentalEditorContext context, MarkerPlaybackCoordinator playback, IMessenger messenger, IUiScheduler scheduler)
    {
        this.context = context; snapshot = context.Track; this.playback = playback; this.messenger = messenger; this.scheduler = scheduler;
        SelectedSubdivisionIndex = Math.Max(0, Array.IndexOf(new[] { 1, 2, 3, 4, 5, 6, 8, 12, 16 }, context.InitialSubdivision)); ZoomFactor = context.InitialZoomFactor; Position = context.InitialPosition;
    }
    public async Task InitializeAsync(CancellationToken token)
    {
        if (registered || closed) return; registered = true;
        externalCancellation = token.Register(() => lifetime.Cancel());
        messenger.Register<InstrumentalTrackChangedMessage>(this, (_, m) =>
        {
            if (closed || m.Snapshot.Context != snapshot.Context || m.Snapshot.Revision < snapshot.Revision) return;
            snapshot = m.Snapshot; OnPropertyChanged(nameof(Notes)); OnPropertyChanged(nameof(KeyFiles)); OnPropertyChanged(nameof(PopupTitle)); OnPropertyChanged(nameof(MappingStatus)); OnPropertyChanged(nameof(EditingHint));
            if (SelectedNoteId is { } id && !Notes.Any(n => n.Id == id)) SelectedNoteId = null;
            if (configured) QueuePreparation();
        });
        messenger.Register<InstrumentalEditResultMessage>(this, (_, m) =>
        { if (closed || m.Context != snapshot.Context || m.RequestId != pendingRequest) return; ErrorMessage = m.Error; if (m.Error is null) SelectedNoteId = m.SelectedNoteId; });
        messenger.Register<InstrumentalSaveStateChangedMessage>(this, (_, m) =>
        { if (closed || m.Context != snapshot.Context || m.Version < saveVersion) return; saveVersion = m.Version; IsSaving = m.IsSaving; SaveError = m.Error; OnPropertyChanged(nameof(SaveStatus)); });
        messenger.Send(new InstrumentalEditorReadyMessage(snapshot.Context));
        try
        {
            lifetime.Token.ThrowIfCancellationRequested();
            playback.Audio.Open(context.AudioPath); playback.Audio.SetSongVolume(context.SongVolume); playback.Audio.SetSamplesVolume(context.SamplesVolume); playback.Audio.SetApplicationVolume(context.ApplicationVolume);
            playback.Audio.SetControlPoints(context.ControlPoints);
            await playback.ConfigureAsync(new(Offsets, Bpms), Waveform.Duration, lifetime.Token);
            configured = true; playback.StateChanged += OnPlaybackChanged; QueuePreparation(); await AwaitPreparationsAsync();
            if (!closed && !lifetime.IsCancellationRequested) { await playback.SeekAsync(context.InitialPosition, lifetime.Token); RefreshPlayback(); }
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        catch (Exception error) { configured = false; ErrorMessage = error.Message; }
        finally { NotifyTransport(); }
    }
    private long EventId(Guid id) { if (!eventIds.TryGetValue(id, out var value)) eventIds[id] = value = nextEventId++; return value; }
    private void QueuePreparation()
    {
        if (closed || !configured) return;
        var events = InstrumentalPlaybackEvents.Build(KeyFiles, Notes, snapshot.TrackVolume, snapshot.AvailableFiles, EventId);
        if (requestedEvents is not null && requestedEvents.SequenceEqual(events)) return;
        requestedEvents = events; preparationCancellation?.Cancel(); preparationCancellation?.Dispose();
        preparationCancellation = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        var generation = ++prepareGeneration; preparing = true; NotifyTransport();
        preparation = ObservePreparationsAsync(preparation, PrepareAsync(events, generation, preparationCancellation.Token));
    }
    private static async Task ObservePreparationsAsync(Task previous, Task current) { await previous; await current; }
    private async Task PrepareAsync(IReadOnlyList<SamplePlaybackEvent> events, int generation, CancellationToken token)
    {
        try
        {
            await playback.UpdateSamplesAsync(snapshot.Context.ProjectPath, events, token);
            await scheduler.InvokeAsync(() =>
            {
                if (closed || generation != prepareGeneration) return;
                var state = playback.Audio.GetSnapshot();
                ErrorMessage = state.Error;
                PlaybackWarning = state.Warning;
            }, CancellationToken.None);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception error) { if (!closed && generation == prepareGeneration) await scheduler.InvokeAsync(() => ErrorMessage = error.Message, CancellationToken.None); }
        finally { if (generation == prepareGeneration) await scheduler.InvokeAsync(() => { preparing = false; NotifyTransport(); }, CancellationToken.None); }
    }
    private async Task AwaitPreparationsAsync() { while (true) { var current = preparation; await current; if (ReferenceEquals(current, preparation)) return; } }
    private void OnPlaybackChanged() => notifications = RefreshNotificationAsync(notifications);
    private async Task RefreshNotificationAsync(Task previous) { await previous; if (!closed) await scheduler.InvokeAsync(() => { if (!closed) RefreshPlayback(); }, CancellationToken.None); }
    public void RefreshPlayback() { if (closed) return; var state = playback.Latest.Playback; Position = state.Position; IsPlaying = state.IsPlaying; PlaybackWarning = state.Warning; if (state.Error is not null) ErrorMessage = state.Error; }
    private void NotifyTransport() { OnPropertyChanged(nameof(CanPlay)); OnPropertyChanged(nameof(CanEdit)); PlayPauseCommand.NotifyCanExecuteChanged(); StopCommand.NotifyCanExecuteChanged(); SeekCommand.NotifyCanExecuteChanged(); NextGridCommand.NotifyCanExecuteChanged(); PreviousGridCommand.NotifyCanExecuteChanged(); }
    [RelayCommand(CanExecute = nameof(CanPlay))]
    private async Task PlayPauseAsync()
    { try { await AwaitPreparationsAsync(); if (IsPlaying) await playback.PauseAsync(lifetime.Token); else await playback.PlayAsync(lifetime.Token); RefreshPlayback(); } catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { } catch (Exception e) { ErrorMessage = e.Message; } }
    [RelayCommand(CanExecute = nameof(CanPlay))]
    private async Task StopAsync() { try { await playback.StopAsync(lifetime.Token); RefreshPlayback(); } catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { } catch (Exception e) { ErrorMessage = e.Message; } }
    [RelayCommand(CanExecute = nameof(CanPlay))]
    private async Task SeekAsync(TimeSpan value) { try { await playback.SeekAsync(value, lifetime.Token); RefreshPlayback(); } catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { } catch (Exception e) { ErrorMessage = e.Message; } }
    [RelayCommand] private void ZoomIn() => ZoomFactor *= 1.25;
    [RelayCommand(CanExecute = nameof(CanPlay))] private Task NextGridAsync() => NavigateAsync(true);
    [RelayCommand(CanExecute = nameof(CanPlay))] private Task PreviousGridAsync() => NavigateAsync(false);
    private Task NavigateAsync(bool forward) => BeatGridCalculator.GetAdjacentPosition(Offsets, Bpms, Subdivision, Position, Waveform.Duration, forward) is { } next ? SeekAsync(next) : Task.CompletedTask;
    [RelayCommand] private void ZoomOut() => ZoomFactor /= 1.25;
    public void SelectNote(Guid? id) => SelectedNoteId = id is null || Notes.Any(n => n.Id == id) ? id : null;
    public void PlaceNote(string key, double ms) { if (!CanEdit) return; pendingRequest = Guid.NewGuid(); messenger.Send(new PlaceInstrumentalNoteRequestedMessage(snapshot.Context, pendingRequest, key, ms, Subdivision)); }
    public void MoveNote(Guid id, string key, double ms) { if (!CanEdit) return; pendingRequest = Guid.NewGuid(); messenger.Send(new MoveInstrumentalNoteRequestedMessage(snapshot.Context, pendingRequest, id, key, ms, Subdivision)); }
    public void RemoveNote(Guid id) { if (!CanEdit) return; pendingRequest = Guid.NewGuid(); messenger.Send(new RemoveInstrumentalNoteRequestedMessage(snapshot.Context, pendingRequest, id)); }
    public Task DeactivateAsync() => deactivation ??= DeactivateCoreAsync();
    private async Task DeactivateCoreAsync()
    {
        if (closed) return; closed = true; lifetime.Cancel(); preparationCancellation?.Cancel(); messenger.UnregisterAll(this); playback.StateChanged -= OnPlaybackChanged; NotifyTransport();
        try { await preparation; await notifications; await playback.StopAsync(CancellationToken.None); }
        finally { try { await playback.DisposeAsync(); } finally { playback.Audio.Dispose(); externalCancellation.Dispose(); preparationCancellation?.Dispose(); lifetime.Dispose(); } }
    }
}
