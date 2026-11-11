using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Hitbench.Core.Entities;
using Hitbench.Services;
using Hitbench.Services.FileSystem;
using Hitbench.Services.Ui;
using Hitbench.Services.Dialogs;
using Hitbench.Services.ProjectFiles;
using Hitbench.Resources.Messages;
using CommunityToolkit.Mvvm.Messaging;
using System.Collections.ObjectModel;
using System.IO;

using Hitbench.Resources.DataModels;

namespace Hitbench.ViewModels.Shell;

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
    IOHsExportDialogService? exportDialog = null,
    IOHsExportDirectoryLauncher? directoryLauncher = null,
    IOHsTrackAppearanceDialogService? trackAppearanceDialog = null, Func<TimeSpan, CancellationToken, Task>? saveDelay = null, IOHsSampleDropImportDialogService? sampleDropDialog = null, OHsAudioOperationProgress? audioOperationProgress = null, IOHsSkinDefaultsService? skinDefaults = null, OHsMapsetSampleImportService? mapsetImports = null, IOHsHitNormalDialogService? hitNormalDialog = null,
    IOHsMidiFileReader? midiReader = null, IOHsMidiImportDialogService? midiDialogs = null) : ObservableObject
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
    public bool CanAddTrack => CanEditTracks;
    public bool CanRemoveTrack => !folderRenamesActive && !IsNodeEditBusy && CanEditTracks && !importBusy && !removalBusy && trackRemovalDialog is not null;
    public bool CanPlaceSamples => !folderRenamesActive && !IsNodeEditBusy && CanEditTracks && Waveform is not null && !importBusy && !removalBusy;
    public bool HasPendingSaves => hitNormalBusy || IsExporting || playbackVolumes.HasPendingSaves || folderRenamesActive || instrumentBusy || managingSamples || IsNodeEditBusy || pendingZoom is not null || importBusy || removalBusy || !saveTail.IsCompleted || lastSaveError is not null;
    [ObservableProperty] private string? saveError;
    private CancellationTokenSource? loadCancellation;
    private Task? activeLoad;
    private int loadGeneration;
    private bool deactivated;
    private Task? deactivation;
    private PlaybackMarkerClock markerClock = new([]);


    [ObservableProperty] private WaveformData? waveform;
    [ObservableProperty] private ProjectData? project;
    [ObservableProperty] private ProjectLoadPhase loadPhase = ProjectLoadPhase.PreparingData;
    public int LoadGeneration => loadGeneration;
    public bool IsLoading => LoadPhase is ProjectLoadPhase.PreparingData or ProjectLoadPhase.AwaitingPresentation;
    public bool IsInteractionReady => LoadPhase == ProjectLoadPhase.Ready && !deactivated && !IsReturning && !reloadingSamples && !IsExporting && !IsRefreshingSkinDefaults && !hitNormalBusy && !IsMidiImportBusy;
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
        await CancelMidiImportAsync();
        await SettleSkinDefaultsAsync();
        await CancelDroppedImportAsync();
        await CancelMapsetImportAsync();
        if (deactivated) return;
        IsAdditionMode = false;
        ResetSampleConfiguration();
        trackAppearanceCancellation?.Cancel();
        await SettleFolderOpenAsync();
        await SettleHitNormalAsync();
        await SettleExportAsync();
        if (reloadingSamples && token != reloadSamplesCancellation?.Token) reloadSamplesCancellation?.Cancel();
        await SettleControlPointsAsync();
        controlPointIds.Clear();
        await SettleWorkstationDrawersAsync();
        CommitPendingZoom();
        await DrainProjectSaveQueueAsync();
        await SettleInstrumentalFoldersAsync();
        sampleSession = Guid.NewGuid();
        ClearInstrumentalHistories(); instrumentIds.Clear(); instrumentNoteIds.Clear(); instrumentEventIds.Clear(); instrumentRevision=0;
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
        await CancelMidiImportAsync();
        await SettleSkinDefaultsAsync();
        await CancelDroppedImportAsync();
        await CancelMapsetImportAsync();
        ResetSampleConfiguration();
        trackAppearanceCancellation?.Cancel();
        await SettleFolderOpenAsync();
        await SettleHitNormalAsync();
        await SettleExportAsync();
        reloadSamplesCancellation?.Cancel();
        await SettleControlPointsAsync();
        await SettleWorkstationDrawersAsync();
        await SettleInstrumentalFoldersAsync();
        deactivated = true; ClearInstrumentalHistories();
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
        if (trackAppearanceTask is not null) await trackAppearanceTask;
        await playbackUpdates;
        if (messenger is not null) messenger.UnregisterAll(this);
        try { await FlushSavesAsync(); }
        finally
        {
            try { await playback.DisposeAsync(); }
            catch (Exception error) { System.Diagnostics.Trace.TraceWarning("Playback cleanup: {0}", error); }
            try { audioService.Stop(); }
            catch (Exception error) { System.Diagnostics.Trace.TraceWarning("Audio stop cleanup: {0}", error); }
            try { audioService.Dispose(); }
            catch (Exception error) { System.Diagnostics.Trace.TraceWarning("Audio disposal: {0}", error); }
            nodeEditCancellation?.Dispose();
            nodeEditCancellation = null;
            loadCancellation?.Dispose();
            loadCancellation = null;
            reloadSamplesCancellation?.Dispose();
            reloadSamplesCancellation = null;
            playbackUpdateCancellation?.Dispose();
            playbackUpdateCancellation = null;
            await uiScheduler.InvokeAsync(NotifyCommandState, CancellationToken.None);
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
        await midiImportOperation;
        await hitNormalOperation;
        await skinDefaultsOperation;
        await playbackVolumes.FlushAsync();
        if(instrumentWorkflow is { } workflow) await workflow;
        if (activeManager is not null) await activeManager;
        await SettleInstrumentalFoldersAsync();
        await sampleChangeNotifications;
        CommitPendingZoom();
        if (activeImport is not null) await activeImport;
        if (activeRemoval is not null) await activeRemoval;
        if (activeNodeEdit is not null) await activeNodeEdit;
        if (trackAppearanceTask is not null) await trackAppearanceTask;
        await AwaitProjectSavesAsync();
        if (lastSaveError is not null) throw new IOException("Could not save project.", lastSaveError);
    }

    public Task FlushPlaybackUpdatesAsync() => playbackUpdates;

    [ObservableProperty] private bool isReturning;
    [ObservableProperty] private bool isSavingForReturn;

    partial void OnIsReturningChanged(bool value)
    {
        if (value) midiImportCancellation?.Cancel();
        if (value) { hitNormalCancellation?.Cancel(); mapsetImportCancellation?.Cancel(); trackAppearanceCancellation?.Cancel(); nodeEditCancellation?.Cancel(); folderOpenCancellation?.Cancel(); }
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
        await CancelMidiImportAsync();
        await SettleControlPointsAsync();
        reloadSamplesCancellation?.Cancel();
        if (activeSampleReload is not null) await activeSampleReload;
        await SettleWorkstationDrawersAsync();
        await SettleInstrumentalFoldersAsync();
        IsReturning = true;
        var prepared = false;
        try
        {
            await WaitForReturnAsync(SettleHitNormalAsync());
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
                await WaitForReturnAsync(AwaitProjectSavesAsync());
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

    private void QueuePlaybackUpdate(bool forceReload = false)
    {
        if (Project is null || projectPath is null || deactivated) return;
        playbackUpdateCancellation?.Cancel();
        playbackUpdateCancellation?.Dispose();
        playbackUpdateCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            loadCancellation?.Token ?? CancellationToken.None);
        var update = UpdatePlaybackAsync(projectPath, loadGeneration, playbackUpdateCancellation.Token, forceReload);
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
                playbackPreparationFailed = !audioService.IsPrepared && playbackError?.StartsWith("Unable to prepare audio:", StringComparison.Ordinal) == true;
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
        var hasSolo = tracks.Any(track => track.IsTrackSoloed && CanInteractWithTrack(track));
        var currentSamples = tracks.SelectMany(track => track.Samples).ToHashSet(ReferenceEqualityComparer.Instance);
        foreach (var removed in sampleEventIds.Keys.Where(sample => !currentSamples.Contains(sample)).ToArray())
            sampleEventIds.Remove(removed);
        var events = new List<SamplePlaybackEvent>(currentSamples.Count);
        foreach(var removed in instrumentIds.Keys.Where(t=>!tracks.Contains(t)).ToArray())
            instrumentIds.Remove(removed);
        var alive = tracks.OfType<InstrumentalTrack>().SelectMany(t=>t.Notes).ToHashSet();
        foreach(var removed in instrumentNoteIds.Keys.Where(n=>!alive.Contains(n)).ToArray())
        { instrumentEventIds.Remove(instrumentNoteIds[removed]); instrumentNoteIds.Remove(removed); }
        var availableLookup = sampleFiles is null ? null : availableSamples.Concat(skinDefaultFiles).Distinct(StringComparer.OrdinalIgnoreCase)
            .ToDictionary(f => f, f => f, StringComparer.OrdinalIgnoreCase);
        foreach(var instrument in tracks.OfType<InstrumentalTrack>())
        {
            if(instrument.IsTrackMuted || hasSolo && !instrument.IsTrackSoloed) continue;
            events.AddRange(InstrumentalPlaybackEvents.BuildWithLookup(instrument.KeyFiles,NoteSnapshots(instrument),instrument.Volume,
                availableLookup,InstrumentEventId, Waveform?.Duration.TotalMilliseconds ?? 0));
        }
        foreach (var track in tracks)
        {
            if (!CanInteractWithTrack(track) || track.IsTrackMuted || hasSolo && !track.IsTrackSoloed) continue;
            if (sampleFiles is not null && GetSampleAvailability(track) != Resources.DataModels.TrackSampleAvailability.Available) continue;
            foreach (var sample in track.Samples)
            {
            if (!sampleEventIds.TryGetValue(sample, out var id))
            {
                id = nextSampleEventId++;
                sampleEventIds.Add(sample, id);
            }
                var file = track.UsesDefaultSample && Project is { } project
                    ? Services.OHsDefaultSamples.Resolve(skinDefaultFiles, Services.OHsDefaultSamples.GetSampleset(project, track, (decimal)sample.Timestamp + 5), track.SampleType)
                    : sample.FileName;
                if (file is not null) events.Add(new SamplePlaybackEvent(id, file, sample.Timestamp, track.Volume));
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
        ReplaceSampleChoices(sampleTrackFiles, choices.Where(name => IsDirectSample(name) || !HasMapsetFolderConflict && OHsMapsetSamples.IsMapsetSamplePath(name)).ToArray());
        if (initializeSelections)
            foreach (var track in tracks.Where(t=>t is not InstrumentalTrack))
                if (!track.UsesDefaultSample) track.SelectedSampleFileName ??= track.Samples.FirstOrDefault()?.FileName ?? sampleTrackFiles.FirstOrDefault();
        OnPropertyChanged(nameof(SampleFiles));
        OnPropertyChanged(nameof(TrackSampleChoices));
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
                instrumentTiming = new([]);
                availableSamples.Clear();
                sampleTrackFiles.Clear();
                RefreshControlPointTimestamps();
                markerClock = new([]);
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
            var skinSnapshot = skinDefaults is null ? new OHsSkinDefaultsSnapshot(source.Project.CurrentSkin, [], false)
                : await skinDefaults.RefreshAsync(projectPath, source.Project.CurrentSkin, source.Project.AllowDefaults, false, token);
            var skinChanged = source.Project.CurrentSkin != skinSnapshot.SkinName;
            source.Project.CurrentSkin = skinSnapshot.SkinName;
            var addedOffsetControlPoint = source.Project.EnsureOffsetControlPoint();
            var waveform = await audioService.AnalyzeAsync(source.AudioPath, token);
            if(source.Project.Tracks.OfType<InstrumentalTrack>().SelectMany(t=>t.Notes).Any(n=>!n.IsReadOnly && n.Timestamp>=waveform.Duration.TotalMilliseconds))
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
                skinDefaultFiles = skinSnapshot.Files;
                instrumentTiming = new(Project.UninheritedPoints);
                latestRequestedLastChanged = Project.LastChanged;
                InitializeControlPoints();
                PublishControlPointTimeline();
                RestoreZoom(Project.WorkstationZoom);
                RestoreMarkerMode(Project.IsMarkerStatic);
                InitializeTrackPresentation(Project);
                markerClock = new PlaybackMarkerClock(Project.UninheritedPoints);
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
                    messenger.Register<AddSampleTrackRequestedMessage>(this, (_, m) => { if(m.Sample is {} sample) TryAddSampleTrack(sample); else AddSampleTrackCommand.Execute(null); });
                    messenger.Register<AddPianoTrackRequestedMessage>(this, (_, _) => AddPianoTrackCommand.Execute(null));
                    messenger.Register<LoadSampleRequestedMessage>(this, (_, _) => LoadSampleCommand.Execute(null));
                    messenger.Register<ManageSamplesRequestedMessage>(this, (_, _) => ManageSamplesCommand.Execute(null));
                    messenger.Register<ProjectSamplesChangedMessage>(this, (_, change) => ReceiveSampleChange(change));
                    messenger.Register<OHsExportResultMessage>(this, (_, result) => ReceiveExportResult(result));
                    RegisterInstrumentMessages();
                    RegisterKeyMappingMessages();
                    RegisterSkinDefaultsMessages();

                }
            });
            await playback.ConfigureAsync(markerClock, waveform.Duration, token);
            if (sampleFiles is not null)
            {
                var choices = await sampleFiles.ListAsync(projectPath, token, includeSubfolders: true);
                await CommitAsync(generation, token, () =>
                {
                    RefreshAvailableSamples(choices);
                });
            }
            await UpdatePlaybackAsync(projectPath, generation, token, skinSnapshot.WasReplaced);
            if (audioService.GetSnapshot().Error?.StartsWith("Unable to prepare audio:", StringComparison.Ordinal) == true)
                await CommitAsync(generation, CancellationToken.None, () => Waveform = null);
            if (source.RequiresTimingMigration && Waveform is not null && projectFiles is not null)
            {
                token.ThrowIfCancellationRequested();
                if (generation != loadGeneration || deactivated) return;
                await projectFiles.SaveAsync(projectPath, ProjectDataSerializer.Serialize(source.Project), token);
            }
            else if (addedOffsetControlPoint || skinChanged)
                await CommitAsync(generation, token, () => { if (Waveform is not null) QueueSave(isChange: false); });

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
        NotifyFolderCommands();
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
        OnPropertyChanged(nameof(CanToggleScrollWithMarker));
        OnPropertyChanged(nameof(CanZoom));
        OnPropertyChanged(nameof(CanPlaceSamples));
    }

    private void NotifyEditingState()
    {
        OnPropertyChanged(nameof(CanImportMidi)); ImportMidiCommand.NotifyCanExecuteChanged();
        NotifyHitNormalCommand();
        NotifyFolderCommands();
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
        OnPropertyChanged(nameof(CanImportDroppedSamples));
        OnPropertyChanged(nameof(CanImportFromMapset)); ImportFromMapsetCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(CanAddTrack));
        OnPropertyChanged(nameof(CanRemoveTrack));
        OnPropertyChanged(nameof(CanPlaceSamples));
        LoadSampleCommand.NotifyCanExecuteChanged();
        AddSampleTrackCommand.NotifyCanExecuteChanged();
        AddPianoTrackCommand.NotifyCanExecuteChanged();
    }
}
