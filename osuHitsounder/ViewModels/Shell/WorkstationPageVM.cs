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

using osuHitsounder.Resources.DataModels;

namespace osuHitsounder.ViewModels.Shell;

public partial class WorkstationPageVM(
    IWorkstationSourceService sourceService,
    MarkerPlaybackCoordinator playback,
    IUiScheduler uiScheduler,
    ISampleFileService? sampleFiles = null,
    ISampleFileDialogService? sampleDialog = null,
    IProjectFileService? projectFiles = null,
    IMessenger? messenger = null,
    ITrackRemovalDialogService? trackRemovalDialog = null,
    PlaybackVolumeSettings? playbackVolumes = null,
    Func<TimeSpan, CancellationToken, Task>? zoomDelay = null,
    INodePasteDialogService? nodePasteDialog = null,
    ISampleManagementDialogService? sampleManagementDialog = null,
    SampleImportBatch? sampleImports = null,
    IInstrumentalTrackDialogService? instrumentDialogs = null,
    Func<TimeSpan, CancellationToken, Task>? folderRenameDelay = null,
    TimeProvider? timeProvider = null,
    IOHsExportDialogService? exportDialog = null) : ObservableObject
{
    private readonly IWorkstationAudioService audioService = playback.Audio;
    private bool subscribedToPlayback;
    private readonly object notificationLock = new();
    private Task notifications = Task.CompletedTask;
    private (long Generation, AudioPlaybackSnapshot Playback)? presentedPlayback;
    private readonly PlaybackVolumeSettings playbackVolumes = playbackVolumes ?? new PlaybackVolumeSettings();
    private readonly ObservableCollection<string> availableSamples = [];
    private readonly ObservableCollection<string> sampleTrackFiles = [];
    private string? projectPath;
    public string ProjectName => projectPath is null ? "" : Path.GetFileName(Path.GetDirectoryName(Path.GetFullPath(projectPath))) ?? "";
    public int ControlPointCount => Project?.ControlPoints.Count ?? 0;
    public string CurrentBpmText => markerClock.GetBpm(Position) is { } bpm ? string.Format(System.Globalization.CultureInfo.CurrentCulture, "{0} BPM", bpm) : "";
    private Task saveTail = Task.CompletedTask;
    private Task<IReadOnlyList<string>>? activeImport;
    private Exception? lastSaveError;
    private bool importBusy;
    private readonly Dictionary<Sample, long> sampleEventIds = new(ReferenceEqualityComparer.Instance);
    private CancellationTokenSource? playbackUpdateCancellation;
    private Task playbackUpdates = Task.CompletedTask;
    private CancellationTokenSource? pendingPlayCancellation;
    private bool playbackPreparationFailed;
    private long nextSampleEventId = 1;
    private int samplesRevision;
    private string? playbackErrorMessage;
    public ObservableCollection<string> SampleFiles => sampleTrackFiles;
    public int SamplesRevision => samplesRevision;
    public bool CanEditTracks => Project is not null && IsInteractionReady;
    public bool CanImportSample => !folderRenamesActive && !instrumentBusy && !managingSamples && !IsNodeEditBusy && CanEditTracks && !importBusy && !removalBusy &&
        sampleFiles is not null && sampleDialog is not null && projectPath is not null;
    public bool CanAddTrack => CanEditTracks && sampleTrackFiles.Count > 0;
    public bool CanRemoveTrack => !folderRenamesActive && !managingSamples && !IsNodeEditBusy && CanEditTracks && !importBusy && !removalBusy && trackRemovalDialog is not null;
    public bool CanPlaceSamples => !folderRenamesActive && !managingSamples && !IsNodeEditBusy && CanEditTracks && Waveform is not null && !importBusy && !removalBusy;
    public bool HasPendingSaves => IsExporting || playbackVolumes.HasPendingSaves || folderRenamesActive || instrumentBusy || managingSamples || IsNodeEditBusy || pendingZoom is not null || importBusy || removalBusy || !saveTail.IsCompleted || lastSaveError is not null;
    [ObservableProperty] private string? saveError;
    private CancellationTokenSource? loadCancellation;
    private Task? activeLoad;
    private int loadGeneration;
    private bool deactivated;
    private Task? deactivation;
    private PlaybackMarkerClock markerClock = new([], []);


    [ObservableProperty] private WaveformData? waveform;
    [ObservableProperty] private ProjectData? project;
    [ObservableProperty] private ProjectLoadPhase loadPhase = ProjectLoadPhase.PreparingData;
    public int LoadGeneration => loadGeneration;
    public bool IsLoading => LoadPhase is ProjectLoadPhase.PreparingData or ProjectLoadPhase.AwaitingPresentation;
    public bool IsInteractionReady => LoadPhase == ProjectLoadPhase.Ready && !deactivated && !IsReturning && !reloadingSamples && !IsExporting;
    public bool CanGoBack => !deactivated && !IsReturning;
    public bool ShowStatusOverlay => IsLoading || IsSavingForReturn;
    public string StatusText => IsSavingForReturn ? "Saving..." : "Loading Project...";

    partial void OnLoadPhaseChanged(ProjectLoadPhase value)
    {
        OnPropertyChanged(nameof(IsLoading));
        OnPropertyChanged(nameof(ShowStatusOverlay));
        OnPropertyChanged(nameof(IsInteractionReady));
        NotifyEditingState();
        NotifyCommandState();
    }

    public void AcknowledgePresentationReady(int generation)
    {
        if (!deactivated && generation == loadGeneration && LoadPhase == ProjectLoadPhase.AwaitingPresentation)
            LoadPhase = ProjectLoadPhase.Ready;
    }
    [ObservableProperty] private string? errorMessage;
    [ObservableProperty] private string? playbackWarning;
    private double zoomFactor = 32;

    public double ZoomFactor => zoomFactor;
    public bool CanPlay => Waveform is not null && IsInteractionReady && !IsInstrumentalTransportBlocked;
    public bool CanZoom => Waveform is not null && IsInteractionReady;

    public async Task LoadAsync(string projectPath, CancellationToken token)
    {
        if (deactivated) return;
        await SettleExportAsync();
        if (reloadingSamples && token != reloadSamplesCancellation?.Token) reloadSamplesCancellation?.Cancel();
        await SettleControlPointsAsync();
        controlPointIds.Clear();
        await SettleInstrumentalEditorAsync();
        await DrainProjectSaveQueueAsync();
        await SettleSampleManagerAsync();
        await SettleInstrumentalFoldersAsync();
        sampleSession = Guid.NewGuid();
        instrumentHistories.Clear(); instrumentIds.Clear(); instrumentNoteIds.Clear(); instrumentEventIds.Clear(); instrumentRevision=0;
        ResetNodeEditing();
        history.Clear();
        NotifyHistoryState();
        CommitPendingZoom();
        pendingPlayCancellation?.Cancel();
        lastGridTarget = null;
        lastGridAudioPosition = null;
        playbackUpdateCancellation?.Cancel();
        loadCancellation?.Cancel();
        loadCancellation?.Dispose();
        loadCancellation = CancellationTokenSource.CreateLinkedTokenSource(token);
        var generation = ++loadGeneration;
        this.projectPath = projectPath;
        OnPropertyChanged(nameof(ProjectName));
        activeLoad = LoadCoreAsync(projectPath, generation, loadCancellation.Token);
        await activeLoad;
    }

    public Task DeactivateAsync() => deactivation ??= DeactivateCoreAsync();

    private async Task DeactivateCoreAsync()
    {
        if (deactivated) return;
        await SettleExportAsync();
        reloadSamplesCancellation?.Cancel();
        await SettleControlPointsAsync();
        await SettleInstrumentalEditorAsync();
        await SettleSampleManagerAsync();
        await SettleInstrumentalFoldersAsync();
        deactivated = true; instrumentHistories.Clear();
        ResetNodeEditing();
        history.Clear();
        NotifyHistoryState();
        OnPropertyChanged(nameof(CanGoBack));
        LoadPhase = ProjectLoadPhase.Cancelled;
        pendingPlayCancellation?.Cancel();
        OnPropertyChanged(nameof(CanRemoveTrack));
        loadGeneration++;
        loadCancellation?.Cancel();
        playbackUpdateCancellation?.Cancel();
        playback.StateChanged -= OnPlaybackStateChanged;
        Task pendingNotifications;
        lock (notificationLock) pendingNotifications = notifications;
        await pendingNotifications;
        if (activeLoad is not null)
        {
            try { await activeLoad; }
            catch (OperationCanceledException) { }
        }
        if (activeSampleReload is not null) await activeSampleReload;
        if (activeRemoval is not null) await activeRemoval;
        if (activeNodeEdit is not null) await activeNodeEdit;
        await playbackUpdates;
        if (messenger is not null) messenger.UnregisterAll(this);
        try { await FlushSavesAsync(); }
        finally
        {
            await playback.DisposeAsync();
            audioService.Stop();
            audioService.Dispose();
            nodeEditCancellation?.Dispose();
            nodeEditCancellation = null;
            loadCancellation?.Dispose();
            loadCancellation = null;
            reloadSamplesCancellation?.Dispose();
            reloadSamplesCancellation = null;
            playbackUpdateCancellation?.Dispose();
            playbackUpdateCancellation = null;
            NotifyCommandState();
        }
    }

    [RelayCommand(CanExecute = nameof(CanZoomIn))]
    private void ZoomIn()
    {
        if (!CanZoomIn()) return;
        SetZoom((int)ZoomFactor + 1);
    }

    [RelayCommand(CanExecute = nameof(CanZoomOut))]
    private void ZoomOut()
    {
        if (!CanZoomOut()) return;
        SetZoom((int)ZoomFactor - 1);
    }

    [RelayCommand(CanExecute = nameof(CanImportSample))]
    private Task LoadSampleAsync() => ImportSamplesAsync();

    public Task<IReadOnlyList<string>> ImportSamplesAsync()
    {
        if (!CanImportSample || sampleDialog is null || sampleFiles is null || projectPath is null)
            return Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>());
        var generation = loadGeneration;
        var project = Project;
        var path = projectPath;
        var token = loadCancellation?.Token ?? CancellationToken.None;
        importBusy = true;
        NotifyEditingState();
        activeImport = ImportSamplesCoreAsync(path, project, generation, token);
        return activeImport;
    }

    private async Task<IReadOnlyList<string>> ImportSamplesCoreAsync(string path, ProjectData? project, int generation,
        CancellationToken token)
    {
        List<string> imported = [];
        List<string> failures = [];
        try
        {
            pendingPlayCancellation?.Cancel();
            await playback.PauseAsync(token);
            RefreshPlayback();
            if (!IsCurrentProject(path, project, generation)) return Array.Empty<string>();
            var sources = await sampleDialog!.SelectAsync(new SamplePickerContext(path, project?.BeatmapPath), token);
            if (sources.Count == 0 || !IsCurrentProject(path, project, generation)) return Array.Empty<string>();
            var batch = await (sampleImports ?? new SampleImportBatch(sampleFiles!)).ImportAsync(path, sources, token);
            imported.AddRange(batch.ImportedFiles); failures.AddRange(batch.Failures);
            if (imported.Count > 0)
                await uiScheduler.InvokeAsync(() =>
                {
                    if (IsCurrentProject(path, project, generation)) QueueSave();
                }, CancellationToken.None);
            token.ThrowIfCancellationRequested();
            if (!IsCurrentProject(path, project, generation)) return Array.Empty<string>();
            try
            {
                var choices = await sampleFiles!.ListAsync(path, token, includeSubfolders: false);
                if (!IsCurrentProject(path, project, generation)) return Array.Empty<string>();
                await CommitAsync(generation, token, () => RefreshAvailableSamples(choices, false, includesSubfolders: false));
                QueuePlaybackUpdate();
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception error) { failures.Add($"Could not refresh sample choices: {error.Message}"); }
            if (IsCurrentProject(path, project, generation))
                ErrorMessage = failures.Count == 0 ? null : string.Join(Environment.NewLine, failures);
            return Array.AsReadOnly(imported.ToArray());
        }
        catch (OperationCanceledException) { return Array.Empty<string>(); }
        catch (Exception error)
        {
            if (IsCurrentProject(path, project, generation)) ErrorMessage = error.Message;
            return Array.AsReadOnly(imported.ToArray());
        }
        finally
        {
            importBusy = false;
            NotifyEditingState();
        }
    }

    private bool IsCurrentProject(string path, ProjectData? project, int generation) =>
        !deactivated && generation == loadGeneration && projectPath == path && ReferenceEquals(Project, project);

    public async Task FlushSavesAsync()
    {
        await playbackVolumes.FlushAsync();
        if(instrumentWorkflow is { } workflow) await workflow;
        if (activeManager is not null) await activeManager;
        await SettleInstrumentalFoldersAsync();
        await sampleChangeNotifications;
        CommitPendingZoom();
        if (activeImport is not null) await activeImport;
        if (activeRemoval is not null) await activeRemoval;
        if (activeNodeEdit is not null) await activeNodeEdit;
        await saveTail;
        if (lastSaveError is not null) throw new IOException("Could not save project.", lastSaveError);
    }

    public Task FlushPlaybackUpdatesAsync() => playbackUpdates;

    [ObservableProperty] private bool isReturning;
    [ObservableProperty] private bool isSavingForReturn;

    partial void OnIsReturningChanged(bool value)
    {
        if (value) nodeEditCancellation?.Cancel();
        OnPropertyChanged(nameof(CanGoBack));
        OnPropertyChanged(nameof(IsInteractionReady));
        NotifyEditingState();
        NotifyCommandState();
    }

    partial void OnIsSavingForReturnChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowStatusOverlay));
        OnPropertyChanged(nameof(StatusText));
    }

    public void CancelReturn()
    {
        if (!deactivated)
        {
            IsReturning = false;
            if (controlPointsContext is null) InitializeControlPoints();
            NotifyEditingState();
        }
    }

    public async Task<bool> PrepareToReturnAsync(CancellationToken token)
    {
        if (deactivated || IsReturning || token.IsCancellationRequested) return false;
        await SettleControlPointsAsync();
        reloadSamplesCancellation?.Cancel();
        if (activeSampleReload is not null) await activeSampleReload;
        await SettleInstrumentalEditorAsync();
        await SettleSampleManagerAsync();
        await SettleInstrumentalFoldersAsync();
        IsReturning = true;
        var prepared = false;
        try
        {
            await WaitForReturnAsync(SettleExportAsync());
            pendingPlayCancellation?.Cancel();
            await WaitForReturnAsync(playback.PauseAsync(CancellationToken.None));
            RefreshPlayback();
            if (IsLoading)
            {
                loadCancellation?.Cancel();
                if (activeLoad is not null) await WaitForReturnAsync(activeLoad);
            }
            else if (LoadPhase == ProjectLoadPhase.Ready)
            {
                if (activeImport is not null) await WaitForReturnAsync(activeImport);
                if (activeRemoval is not null) await WaitForReturnAsync(activeRemoval);
                if (activeNodeEdit is not null) await WaitForReturnAsync(activeNodeEdit);
                await WaitForReturnAsync(playbackUpdates);
                QueueSave(CommitPendingZoom(false));
                await WaitForReturnAsync(saveTail);
                if (lastSaveError is not null) throw new IOException("Could not save project.", lastSaveError);
            }
            if (token.IsCancellationRequested) return false;
            await WaitForReturnAsync(playbackVolumes.FlushAsync());
            prepared = true;
            return true;
        }
        catch (Exception error)
        {
            ErrorMessage = error.Message;
            return false;
        }
        finally
        {
            IsSavingForReturn = false;
            if (!prepared) CancelReturn();
        }
    }

    private async Task WaitForReturnAsync(Task operation)
    {
        if (!operation.IsCompleted) IsSavingForReturn = true;
        await operation;
    }

    private void QueuePlaybackUpdate()
    {
        if (Project is null || projectPath is null || deactivated) return;
        playbackUpdateCancellation?.Cancel();
        playbackUpdateCancellation?.Dispose();
        playbackUpdateCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            loadCancellation?.Token ?? CancellationToken.None);
        var update = UpdatePlaybackAsync(projectPath, loadGeneration, playbackUpdateCancellation.Token);
        playbackUpdates = AwaitPlaybackUpdatesAsync(playbackUpdates, update);
    }

    private async Task UpdatePlaybackAsync(string path, int generation, CancellationToken token, bool forceReload = false)
    {
        var events = CreatePlaybackEvents();
        try
        {
            await playback.UpdateSamplesAsync(path, events, token, forceReload);
            var playbackError = audioService.GetSnapshot().Error;
            await CommitAsync(generation, CancellationToken.None, () =>
            {
                if (token.IsCancellationRequested) return;
                playbackPreparationFailed = playbackError?.StartsWith("Unable to prepare audio:", StringComparison.Ordinal) == true;
                if (!string.IsNullOrWhiteSpace(playbackError))
                {
                    playbackErrorMessage = playbackError;
                    ErrorMessage = playbackError;
                }
                else if (ErrorMessage == playbackErrorMessage)
                {
                    playbackErrorMessage = null;
                    ErrorMessage = null;
                }
            });
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception failure)
        {
            await CommitAsync(generation, CancellationToken.None, () =>
            {
                if (token.IsCancellationRequested) return;
                playbackPreparationFailed = true;
                playbackErrorMessage = failure.Message;
                ErrorMessage = failure.Message;
            });
        }
    }

    private IReadOnlyList<SamplePlaybackEvent> CreatePlaybackEvents()
    {
        var hasSolo = tracks.Any(track => track.IsTrackSoloed);
        var currentSamples = tracks.SelectMany(track => track.Samples).ToHashSet(ReferenceEqualityComparer.Instance);
        foreach (var removed in sampleEventIds.Keys.Where(sample => !currentSamples.Contains(sample)).ToArray())
            sampleEventIds.Remove(removed);
        var events = new List<SamplePlaybackEvent>(currentSamples.Count);
        foreach(var removed in instrumentIds.Keys.Where(t=>!tracks.Contains(t)).ToArray())
            instrumentIds.Remove(removed);
        var alive = tracks.OfType<InstrumentalTrack>().SelectMany(t=>t.Notes).ToHashSet();
        foreach(var removed in instrumentNoteIds.Keys.Where(n=>!alive.Contains(n)).ToArray())
        { instrumentEventIds.Remove(instrumentNoteIds[removed]); instrumentNoteIds.Remove(removed); }
        var availableLookup = sampleFiles is null ? null : availableSamples.Distinct(StringComparer.OrdinalIgnoreCase)
            .ToDictionary(f => f, f => f, StringComparer.OrdinalIgnoreCase);
        foreach(var instrument in tracks.OfType<InstrumentalTrack>())
        {
            if(instrument.IsTrackMuted || hasSolo && !instrument.IsTrackSoloed) continue;
            events.AddRange(InstrumentalPlaybackEvents.BuildWithLookup(instrument.KeyFiles,NoteSnapshots(instrument),instrument.Volume,
                availableLookup,InstrumentEventId));
        }
        foreach (var track in tracks)
        {
            if (track.IsTrackMuted || hasSolo && !track.IsTrackSoloed) continue;
            if (sampleFiles is not null && GetSampleAvailability(track) != Resources.DataModels.TrackSampleAvailability.Available) continue;
            foreach (var sample in track.Samples)
            {
            if (!sampleEventIds.TryGetValue(sample, out var id))
            {
                id = nextSampleEventId++;
                sampleEventIds.Add(sample, id);
            }
                events.Add(new SamplePlaybackEvent(id, sample.FileName, sample.Timestamp, track.Volume));
            }
        }
        return events;
    }

    private static async Task AwaitPlaybackUpdatesAsync(Task previous, Task current)
    {
        try { await previous; } catch (OperationCanceledException) { }
        await current;
    }

    private static bool IsDirectSample(string name) => !name.Contains('/') && !name.Contains('\\');

    private void RefreshAvailableSamples(IReadOnlyList<string> choices, bool initializeSelections = true, bool includesSubfolders = true)
    {
        if (!includesSubfolders)
            choices = choices.Concat(availableSamples.Where(name => !IsDirectSample(name))).ToArray();
        ReplaceSampleChoices(availableSamples, choices);
        ReplaceSampleChoices(sampleTrackFiles, choices.Where(IsDirectSample).ToArray());
        if (initializeSelections)
            foreach (var track in tracks.Where(t=>t is not InstrumentalTrack))
                track.SelectedSampleFileName ??= track.Samples.FirstOrDefault()?.FileName ?? sampleTrackFiles.FirstOrDefault();
        OnPropertyChanged(nameof(SampleFiles));
        OnPropertyChanged(nameof(CanAddTrack));
        AddSampleTrackCommand.NotifyCanExecuteChanged();
        AddPianoTrackCommand.NotifyCanExecuteChanged();
    }

    private static void ReplaceSampleChoices(ObservableCollection<string> target, IReadOnlyList<string> choices)
    {
        for (var index = 0; index < choices.Count; index++)
        {
            if (index < target.Count && target[index] == choices[index]) continue;
            var existing = target.IndexOf(choices[index]);
            if (existing >= 0) target.Move(existing, index);
            else target.Insert(index, choices[index]);
        }
        while (target.Count > choices.Count) target.RemoveAt(target.Count - 1);
    }

    private async Task LoadCoreAsync(string projectPath, int generation, CancellationToken token)
    {
        try
        {
            await CommitAsync(generation, token, () =>
            {
                LoadPhase = ProjectLoadPhase.PreparingData;
                presentedPlayback = null;
                ErrorMessage = null;
                playbackErrorMessage = null;
                Waveform = null;
                Project = null;
                availableSamples.Clear();
                sampleTrackFiles.Clear();
                RefreshControlPointTimestamps();
                markerClock = new([], []);
                OnPropertyChanged(nameof(CurrentBpmText));

                Position = TimeSpan.Zero;
                IsPlaying = false;
                sampleEventIds.Clear();
                nextSampleEventId = 1;
                NotifyCommandState();
            });
            if (!subscribedToPlayback) { playback.StateChanged += OnPlaybackStateChanged; subscribedToPlayback = true; }
            await playback.StopAsync(token);
            var source = await sourceService.LoadAsync(projectPath, token);
            var waveform = await audioService.AnalyzeAsync(source.AudioPath, token);
            if(source.Project.Tracks.OfType<InstrumentalTrack>().SelectMany(t=>t.Notes).Any(n=>n.Timestamp>=waveform.Duration.TotalMilliseconds))
                throw new InvalidDataException("Instrumental note lies beyond the song duration.");
            token.ThrowIfCancellationRequested();
            await CommitAsync(generation, token, () =>
            {
                audioService.Open(source.AudioPath);
                audioService.SetSongVolume(SongVolume);
                audioService.SetApplicationVolume(ApplicationVolume);
                audioService.SetSamplesVolume(SamplesVolume);
                exportBeatmapPath = source.BeatmapPath; exportAudioPath = source.AudioPath;
                Project = source.Project;
                latestRequestedLastChanged = Project.LastChanged;
                InitializeControlPoints();
                PublishControlPointTimeline();
                RestoreZoom(Project.WorkstationZoom);
                InitializeTrackPresentation(Project);
                markerClock = new PlaybackMarkerClock(Project.Offsets, Project.BPMs);
                OnPropertyChanged(nameof(CurrentBpmText));
                SetProperty(ref playbackInterval, markerClock.IsValid ? markerClock.GetInterval(TimeSpan.Zero) : TimeSpan.Zero,
                    nameof(PlaybackInterval));
                if (!markerClock.IsValid)
                    ErrorMessage = "Playback requires valid BPM timing data. Reload a beatmap with timing points.";
                Waveform = waveform;
                InitializeLoadedTracks(source.Project);
                if (messenger is not null)
                {
                    messenger.UnregisterAll(this);
                    messenger.Register<AddSampleTrackRequestedMessage>(this, (_, _) => AddSampleTrackCommand.Execute(null));
                    messenger.Register<AddPianoTrackRequestedMessage>(this, (_, _) => AddPianoTrackCommand.Execute(null));
                    messenger.Register<LoadSampleRequestedMessage>(this, (_, _) => LoadSampleCommand.Execute(null));
                    messenger.Register<ManageSamplesRequestedMessage>(this, (_, _) => ManageSamplesCommand.Execute(null));
                    messenger.Register<ProjectSamplesChangedMessage>(this, (_, change) => ReceiveSampleChange(change));
                    messenger.Register<OHsExportResultMessage>(this, (_, result) => ReceiveExportResult(result));
                    RegisterInstrumentMessages();
                    RegisterKeyMappingMessages();

                }
            });
            await playback.ConfigureAsync(markerClock, waveform.Duration, token);
            if (sampleFiles is not null)
            {
                var choices = await sampleFiles.ListAsync(projectPath, token, includeSubfolders: false);
                if (source.Project.Tracks.OfType<InstrumentalTrack>().Any())
                    choices = await sampleFiles.ListAsync(projectPath, token);
                await CommitAsync(generation, token, () =>
                {
                    RefreshAvailableSamples(choices);
                });
            }
            await UpdatePlaybackAsync(projectPath, generation, token);
            if (audioService.GetSnapshot().Error?.StartsWith("Unable to prepare audio:", StringComparison.Ordinal) == true)
                await CommitAsync(generation, CancellationToken.None, () => Waveform = null);

        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception error)
        {
            await CommitAsync(generation, CancellationToken.None, () => { ErrorMessage = error.Message; Waveform = null; });
        }
        finally
        {
            await CommitAsync(generation, CancellationToken.None, () =>
            {
                LoadPhase = token.IsCancellationRequested ? ProjectLoadPhase.Cancelled
                    : Waveform is not null ? ProjectLoadPhase.AwaitingPresentation : ProjectLoadPhase.Failed;
                OnPropertyChanged(nameof(CanEditTracks));
                NotifyEditingState();
                NotifyCommandState();
            });
        }
    }

    private Task CommitAsync(int generation, CancellationToken token, Action action) =>
        uiScheduler.InvokeAsync(() =>
        {
            if (generation == loadGeneration && !deactivated) action();
        }, token);

    private void NotifyCommandState()
    {
        NotifyExportState();
        NotifyNodeEditingState();
        NotifyHistoryState();
        NotifyTrackCommands();
        PlayPauseCommand.NotifyCanExecuteChanged();
        StopCommand.NotifyCanExecuteChanged();
        SeekCommand.NotifyCanExecuteChanged();
        NextGridLineCommand.NotifyCanExecuteChanged();
        PreviousGridLineCommand.NotifyCanExecuteChanged();
        NextWholeBeatCommand.NotifyCanExecuteChanged();
        PreviousWholeBeatCommand.NotifyCanExecuteChanged();
        ZoomInCommand.NotifyCanExecuteChanged();
        ZoomOutCommand.NotifyCanExecuteChanged();
        IncreaseSubdivisionCommand.NotifyCanExecuteChanged();
        DecreaseSubdivisionCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(CanPlay));
        OnPropertyChanged(nameof(CanZoom));
        OnPropertyChanged(nameof(CanPlaceSamples));
    }

    private void NotifyEditingState()
    {
        NotifyExportState();
        NotifySampleReloadAvailability();
        OnPropertyChanged(nameof(CanOpenControlPoints)); AddControlPointCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(HasPendingSaves)); PublishControlPointsSnapshot();
        OnPropertyChanged(nameof(CanAddInstrumentalTrack)); OnPropertyChanged(nameof(CanOpenInstrumentalEditor));
        ViewArrangementCommand.NotifyCanExecuteChanged(); KeysMappingCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(CanManageSamples)); ManageSamplesCommand.NotifyCanExecuteChanged();
        NotifyNodeEditingState();
        NotifyHistoryState();
        OnPropertyChanged(nameof(CanEditTracks));
        ToggleTracksCollapsedCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(CanImportSample));
        OnPropertyChanged(nameof(CanAddTrack));
        OnPropertyChanged(nameof(CanRemoveTrack));
        OnPropertyChanged(nameof(CanPlaceSamples));
        LoadSampleCommand.NotifyCanExecuteChanged();
        AddSampleTrackCommand.NotifyCanExecuteChanged();
        AddPianoTrackCommand.NotifyCanExecuteChanged();
    }
}
