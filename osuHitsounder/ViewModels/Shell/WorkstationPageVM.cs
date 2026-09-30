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

namespace osuHitsounder.ViewModels.Shell;

public partial class WorkstationPageVM(
    IWorkstationSourceService sourceService,
    IWorkstationAudioService audioService,
    IUiScheduler uiScheduler,
    ISampleFileService? sampleFiles = null,
    ISampleFileDialogService? sampleDialog = null,
    IProjectFileService? projectFiles = null,
    IMessenger? messenger = null,
    ITrackRemovalDialogService? trackRemovalDialog = null,
    PlaybackVolumeSettings? playbackVolumes = null) : ObservableObject
{
    private readonly PlaybackVolumeSettings playbackVolumes = playbackVolumes ?? new PlaybackVolumeSettings();
    private readonly ObservableCollection<Track> tracks = [];
    private readonly ObservableCollection<string> availableSamples = [];
    private string? projectPath;
    private Task saveTail = Task.CompletedTask;
    private Task<string?>? activeImport;
    private Exception? lastSaveError;
    private bool importBusy;
    private TimeSpan? lastGridTarget;
    private TimeSpan? lastGridAudioPosition;
    private bool removalBusy;
    private Task? activeRemoval;
    private readonly Dictionary<Sample, long> sampleEventIds = new(ReferenceEqualityComparer.Instance);
    private CancellationTokenSource? playbackUpdateCancellation;
    private Task playbackUpdates = Task.CompletedTask;
    private CancellationTokenSource? pendingPlayCancellation;
    private bool playbackPreparationFailed;
    private long nextSampleEventId = 1;
    private int samplesRevision;
    private string? playbackErrorMessage;
    public ObservableCollection<Track> Tracks => tracks;
    public ObservableCollection<string> SampleFiles => availableSamples;
    public int SamplesRevision => samplesRevision;
    public int Subdivision => new[] { 1, 2, 3, 4, 5, 6, 8, 12, 16 }[SelectedSubdivisionIndex];
    public bool CanEditTracks => Project is not null && !IsLoading && !deactivated;
    public bool CanImportSample => CanEditTracks && !importBusy && !removalBusy &&
        sampleFiles is not null && sampleDialog is not null && projectPath is not null;
    public bool CanAddTrack => CanEditTracks && availableSamples.Count > 0;
    public bool CanRemoveTrack => CanEditTracks && !importBusy && !removalBusy && trackRemovalDialog is not null;
    public bool CanPlaceSamples => CanEditTracks && Waveform is not null && !importBusy && !removalBusy;
    public bool HasPendingSaves => importBusy || removalBusy || !saveTail.IsCompleted || lastSaveError is not null;
    [ObservableProperty] private string? saveError;
    private CancellationTokenSource? loadCancellation;
    private Task? activeLoad;
    private int loadGeneration;
    private bool deactivated;
    private PlaybackMarkerClock markerClock = new([], []);
    private TimeSpan processedMarker;
    private bool includeMarkerStart = true;
    private TimeSpan playbackInterval;
    public TimeSpan PlaybackInterval => playbackInterval;


    [ObservableProperty] private WaveformData? waveform;
    [ObservableProperty] private ProjectData? project;
    [ObservableProperty] private TimeSpan position;
    [ObservableProperty] private string elapsedText = "00:00.0";
    [ObservableProperty] private bool isLoading;
    [ObservableProperty] private bool isPlaying;
    [ObservableProperty] private string? errorMessage;
    [ObservableProperty] private string? playbackWarning;
    private int songVolume = 100;
    private double zoomFactor = 16;
    private double trackHeight = 72;
    private int selectedSubdivisionIndex = 3;

    public double ZoomFactor => zoomFactor;
    public double TrackHeight => trackHeight;
    public int SongVolume
    {
        get => songVolume;
        set
        {
            var next = Math.Clamp(value, 0, 100);
            if (!SetProperty(ref songVolume, next)) return;
            audioService.SetSongVolume(next);
        }
    }
    public int ApplicationVolume
    {
        get => playbackVolumes.ApplicationVolume;
        set
        {
            var previous = playbackVolumes.ApplicationVolume;
            playbackVolumes.ApplicationVolume = value;
            if (previous == playbackVolumes.ApplicationVolume) return;
            OnPropertyChanged();
            audioService.SetApplicationVolume(playbackVolumes.ApplicationVolume);
        }
    }
    public int SamplesVolume
    {
        get => playbackVolumes.SamplesVolume;
        set
        {
            var previous = playbackVolumes.SamplesVolume;
            playbackVolumes.SamplesVolume = value;
            if (previous == playbackVolumes.SamplesVolume) return;
            OnPropertyChanged();
            audioService.SetSamplesVolume(playbackVolumes.SamplesVolume);
        }
    }
    public int SelectedSubdivisionIndex
    {
        get => selectedSubdivisionIndex;
        set
        {
            var next = Math.Clamp(value, 0, 8);
            if (!SetProperty(ref selectedSubdivisionIndex, next)) return;
            IncreaseSubdivisionCommand.NotifyCanExecuteChanged();
            DecreaseSubdivisionCommand.NotifyCanExecuteChanged();
            OnPropertyChanged(nameof(Subdivision));
        }
    }
    public bool CanPlay => Waveform is not null && !IsLoading && !deactivated;
    public bool CanZoom => Waveform is not null && !IsLoading && !deactivated;

    public Task LoadAsync(string projectPath, CancellationToken token)
    {
        if (deactivated) return Task.CompletedTask;
        pendingPlayCancellation?.Cancel();
        lastGridTarget = null;
        lastGridAudioPosition = null;
        playbackUpdateCancellation?.Cancel();
        loadCancellation?.Cancel();
        loadCancellation?.Dispose();
        loadCancellation = CancellationTokenSource.CreateLinkedTokenSource(token);
        var generation = ++loadGeneration;
        this.projectPath = projectPath;
        activeLoad = LoadCoreAsync(projectPath, generation, loadCancellation.Token);
        return activeLoad;
    }

    public async Task DeactivateAsync()
    {
        if (deactivated) return;
        deactivated = true;
        pendingPlayCancellation?.Cancel();
        OnPropertyChanged(nameof(CanRemoveTrack));
        loadGeneration++;
        loadCancellation?.Cancel();
        playbackUpdateCancellation?.Cancel();
        if (activeLoad is not null)
        {
            try { await activeLoad; }
            catch (OperationCanceledException) { }
        }
        if (activeRemoval is not null) await activeRemoval;
        await playbackUpdates;
        if (messenger is not null) messenger.UnregisterAll(this);
        try { await FlushSavesAsync(); }
        finally
        {
            audioService.Stop();
            audioService.Dispose();
            loadCancellation?.Dispose();
            loadCancellation = null;
            playbackUpdateCancellation?.Dispose();
            playbackUpdateCancellation = null;
            NotifyCommandState();
        }
    }

    public void RefreshPlayback()
    {
        if (deactivated) return;
        var snapshot = audioService.GetSnapshot();
        PlaybackWarning = snapshot.Warning;
        var marker = snapshot.Position;
        var duration = Waveform?.Duration ?? TimeSpan.Zero;
        var terminal = IsPlaying && !snapshot.IsPlaying && snapshot.Position >= duration && duration > TimeSpan.Zero;
        if (IsPlaying && !snapshot.IsPlaying && !terminal && snapshot.Error is not null)
            ResetMarker(snapshot.Position);
        if (markerClock.IsValid && (snapshot.IsPlaying || terminal))
        {
            marker = markerClock.GetPosition(snapshot.Position, duration);
            // A seek can land between grid boundaries; never step back behind its baseline.
            if (marker < processedMarker) marker = processedMarker;
            if (snapshot.IsPlaying)
            {
                var ids = new List<long>();
                foreach (var track in tracks)
                foreach (var sample in track.Samples)
                {
                    var timestamp = TimeSpan.FromMilliseconds(sample.Timestamp);
                    if ((timestamp > processedMarker || includeMarkerStart && timestamp == processedMarker) &&
                        timestamp <= marker && sampleEventIds.TryGetValue(sample, out var id)) ids.Add(id);
                }
                if (ids.Count > 0) audioService.TriggerSamples(ids);
                includeMarkerStart = false;
            }
            processedMarker = marker;
        }
        if (markerClock.IsValid)
            SetProperty(ref playbackInterval, markerClock.GetInterval(snapshot.Position), nameof(PlaybackInterval));
        Position = marker;
        IsPlaying = snapshot.IsPlaying;
        ElapsedText = $"{(int)marker.TotalMinutes:00}:{marker.Seconds:00}.{marker.Milliseconds:000}";
        if (!string.IsNullOrWhiteSpace(snapshot.Error)) ErrorMessage = snapshot.Error;
    }

    private void ResetMarker(TimeSpan position)
    {
        processedMarker = position;
        includeMarkerStart = true;
    }

    [RelayCommand(CanExecute = nameof(CanUseTransport))]
    private async Task PlayPauseAsync()
    {
        if (audioService.GetSnapshot().IsPlaying)
        {
            audioService.Pause();
            // Intentional pause preserves traversal, unlike a device-stopped session.
            IsPlaying = false;
        }
        else
        {
            if (!markerClock.IsValid)
            {
                ErrorMessage = "Playback requires valid BPM timing data. Reload a beatmap with timing points.";
                return;
            }
            pendingPlayCancellation?.Cancel();
            using var cancellation = new CancellationTokenSource();
            pendingPlayCancellation = cancellation;
            var generation = loadGeneration;
            try
            {
                Task pending;
                do
                {
                    pending = playbackUpdates;
                    await pending.WaitAsync(cancellation.Token);
                } while (!ReferenceEquals(pending, playbackUpdates));
                if (cancellation.IsCancellationRequested || deactivated || generation != loadGeneration ||
                    !CanPlay || playbackPreparationFailed) return;
                if (Waveform is not null && audioService.GetSnapshot().Position >= Waveform.Duration)
                {
                    audioService.Stop();
                    ResetMarker(TimeSpan.Zero);
                }
                var start = audioService.GetSnapshot().Position;
                if (start < processedMarker) ResetMarker(start);
                audioService.Play();
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { return; }
            catch (Exception error) { ErrorMessage = error.Message; return; }
            finally
            {
                if (ReferenceEquals(pendingPlayCancellation, cancellation)) pendingPlayCancellation = null;
            }
        }
        RefreshPlayback();
    }

    [RelayCommand(CanExecute = nameof(CanUseTransport))]
    private void Stop()
    {
        pendingPlayCancellation?.Cancel();
        lastGridTarget = null;
        lastGridAudioPosition = null;
        audioService.Stop();
        ResetMarker(TimeSpan.Zero);
        RefreshPlayback();
    }

    [RelayCommand(CanExecute = nameof(CanUseTransport))]
    private void Seek(TimeSpan position)
    {
        if (!CanUseTransport()) return;
        pendingPlayCancellation?.Cancel();
        lastGridTarget = null;
        lastGridAudioPosition = null;
        audioService.Seek(position);
        ResetMarker(audioService.GetSnapshot().Position);
        RefreshPlayback();
    }

    [RelayCommand(CanExecute = nameof(CanUseTransport))]
    private void NextGridLine() => SeekAdjacentGridLine(true, Subdivision);

    [RelayCommand(CanExecute = nameof(CanUseTransport))]
    private void PreviousGridLine() => SeekAdjacentGridLine(false, Subdivision);

    [RelayCommand(CanExecute = nameof(CanUseTransport))]
    private void NextWholeBeat() => SeekAdjacentGridLine(true, 1);

    [RelayCommand(CanExecute = nameof(CanUseTransport))]
    private void PreviousWholeBeat() => SeekAdjacentGridLine(false, 1);

    private void SeekAdjacentGridLine(bool forward, int subdivision)
    {
        if (!CanUseTransport() || Project is null || Waveform is null) return;
        var position = audioService.GetSnapshot().Position;
        if (lastGridTarget is { } lastTarget && lastGridAudioPosition is { } lastAudioPosition &&
            position >= TimeSpan.FromTicks(Math.Min(lastAudioPosition.Ticks, lastTarget.Ticks)) &&
            position <= TimeSpan.FromTicks(Math.Max(lastAudioPosition.Ticks, lastTarget.Ticks)))
            position = lastTarget;
        var target = BeatGridCalculator.GetAdjacentPosition(Project.Offsets, Project.BPMs,
            subdivision, position, Waveform.Duration, forward);
        if (target is not null)
        {
            Seek(target.Value);
            if (audioService.GetSnapshot().Error is null)
            {
                lastGridTarget = target.Value;
                lastGridAudioPosition = Position;
            }
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

    [RelayCommand(CanExecute = nameof(CanIncreaseTrackHeight))]
    private void IncreaseTrackHeight() => SetTrackHeight(trackHeight + 12);

    [RelayCommand(CanExecute = nameof(CanDecreaseTrackHeight))]
    private void DecreaseTrackHeight() => SetTrackHeight(trackHeight - 12);

    [RelayCommand(CanExecute = nameof(CanIncreaseSubdivision))]
    private void IncreaseSubdivision()
    {
        if (!CanIncreaseSubdivision()) return;
        SelectedSubdivisionIndex++;
    }

    [RelayCommand(CanExecute = nameof(CanDecreaseSubdivision))]
    private void DecreaseSubdivision()
    {
        if (!CanDecreaseSubdivision()) return;
        SelectedSubdivisionIndex--;
    }

    [RelayCommand(CanExecute = nameof(CanAddTrack))]
    private void AddSampleTrack()
    {
        if (!CanAddTrack) return;
        var track = new Track { SelectedSampleFileName = availableSamples[0] };
        Project!.Tracks.Add(track);
        tracks.Add(track);
        QueueSave();
    }

    [RelayCommand(CanExecute = nameof(CanAddTrack))]
    private static void AddPianoTrack() { }

    [RelayCommand]
    private Task RemoveTrackAsync(Track track)
    {
        if (!CanRemoveTrack || !tracks.Contains(track)) return Task.CompletedTask;
        activeRemoval = RemoveTrackCoreAsync(track);
        return activeRemoval;
    }

    private async Task RemoveTrackCoreAsync(Track track)
    {
        var project = Project;
        var generation = loadGeneration;
        removalBusy = true;
        OnPropertyChanged(nameof(CanRemoveTrack));
        OnPropertyChanged(nameof(CanImportSample));
        OnPropertyChanged(nameof(CanPlaceSamples));
        try
        {
            if (!await trackRemovalDialog!.ConfirmAsync(loadCancellation?.Token ?? CancellationToken.None)) return;
            if (deactivated || generation != loadGeneration || !ReferenceEquals(Project, project) ||
                project is null || !tracks.Contains(track)) return;
            project.Tracks.Remove(track);
            tracks.Remove(track);
            QueuePlaybackUpdate();
            QueueSave();
        }
        catch (OperationCanceledException) { }
        catch (Exception error) { ErrorMessage = error.Message; }
        finally
        {
            removalBusy = false;
            OnPropertyChanged(nameof(CanRemoveTrack));
            OnPropertyChanged(nameof(CanImportSample));
            OnPropertyChanged(nameof(CanPlaceSamples));
        }
    }

    public void SelectSample(Track track, string fileName)
    {
        if (!CanEditTracks || !tracks.Contains(track) || string.IsNullOrWhiteSpace(fileName) ||
            sampleFiles is not null && !availableSamples.Any(name => string.Equals(name, fileName, StringComparison.OrdinalIgnoreCase))) return;
        if (string.Equals(track.SelectedSampleFileName, fileName, StringComparison.OrdinalIgnoreCase)) return;
        track.SelectedSampleFileName = availableSamples.FirstOrDefault(name =>
            string.Equals(name, fileName, StringComparison.OrdinalIgnoreCase)) ?? fileName;
        QueueSave();
    }

    public bool PlaceSample(Track track, double leftBoundaryMilliseconds)
    {
        if (!CanPlaceSamples || !tracks.Contains(track) || !double.IsFinite(leftBoundaryMilliseconds) ||
            leftBoundaryMilliseconds < 0 || leftBoundaryMilliseconds >= Waveform!.Duration.TotalMilliseconds ||
            leftBoundaryMilliseconds > int.MaxValue) return false;
        var fileName = availableSamples.FirstOrDefault(name =>
            string.Equals(name, track.SelectedSampleFileName, StringComparison.OrdinalIgnoreCase));
        if (fileName is null) return false;
        track.Samples.Add(new Sample
        {
            FileName = fileName,
            Volume = 100,
            Timestamp = (int)Math.Round(leftBoundaryMilliseconds, MidpointRounding.AwayFromZero)
        });
        samplesRevision++;
        OnPropertyChanged(nameof(SamplesRevision));
        QueuePlaybackUpdate();
        QueueSave();
        return true;
    }

    public bool RemoveSample(Track track, Sample sample)
    {
        if (!CanPlaceSamples || !tracks.Contains(track) || !track.Samples.Remove(sample)) return false;
        samplesRevision++;
        OnPropertyChanged(nameof(SamplesRevision));
        QueuePlaybackUpdate();
        QueueSave();
        return true;
    }

    public void SetTrackVolume(Track track, int volume)
    {
        if (!CanEditTracks || !tracks.Contains(track)) return;
        var next = Math.Clamp(volume, 5, 100);
        if (track.Volume == next) return;
        track.Volume = next;
        QueuePlaybackUpdate();
        QueueSave();
    }

    [RelayCommand(CanExecute = nameof(CanImportSample))]
    private Task LoadSampleAsync() => ImportSampleAsync();

    public Task<string?> ImportSampleAsync()
    {
        if (!CanImportSample || sampleDialog is null || sampleFiles is null || projectPath is null)
            return Task.FromResult<string?>(null);
        var generation = loadGeneration;
        var project = Project;
        var path = projectPath;
        var token = loadCancellation?.Token ?? CancellationToken.None;
        importBusy = true;
        NotifyEditingState();
        activeImport = ImportSampleCoreAsync(path, project, generation, token);
        return activeImport;
    }

    private async Task<string?> ImportSampleCoreAsync(string path, ProjectData? project, int generation,
        CancellationToken token)
    {
        try
        {
            var source = await sampleDialog!.SelectAsync(token);
            if (source is null || !IsCurrentProject(path, project, generation)) return null;
            var name = await sampleFiles!.ImportAsync(path, source, token);
            if (!IsCurrentProject(path, project, generation)) return null;
            var choices = await sampleFiles.ListAsync(path, token);
            if (!IsCurrentProject(path, project, generation)) return null;
            await CommitAsync(generation, token, () => RefreshAvailableSamples(choices));
            QueuePlaybackUpdate();
            return name;
        }
        catch (OperationCanceledException) { return null; }
        catch (Exception error) { ErrorMessage = error.Message; return null; }
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
        if (activeImport is not null) await activeImport;
        await saveTail;
        if (lastSaveError is not null) throw new IOException("Could not save project.", lastSaveError);
    }

    public Task FlushPlaybackUpdatesAsync() => playbackUpdates;

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

    private async Task UpdatePlaybackAsync(string path, int generation, CancellationToken token)
    {
        var events = CreatePlaybackEvents();
        try
        {
            await audioService.UpdateSamplesAsync(path, events, token);
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
        var currentSamples = tracks.SelectMany(track => track.Samples).ToHashSet(ReferenceEqualityComparer.Instance);
        foreach (var removed in sampleEventIds.Keys.Where(sample => !currentSamples.Contains(sample)).ToArray())
            sampleEventIds.Remove(removed);
        var events = new List<SamplePlaybackEvent>(currentSamples.Count);
        foreach (var track in tracks)
        foreach (var sample in track.Samples)
        {
            if (!sampleEventIds.TryGetValue(sample, out var id))
            {
                id = nextSampleEventId++;
                sampleEventIds.Add(sample, id);
            }
            events.Add(new SamplePlaybackEvent(id, sample.FileName, sample.Timestamp, sample.Volume, track.Volume));
        }
        return events;
    }

    private static async Task AwaitPlaybackUpdatesAsync(Task previous, Task current)
    {
        try { await previous; } catch (OperationCanceledException) { }
        await current;
    }

    [RelayCommand]
    private void RetrySave() => QueueSave();

    private void QueueSave()
    {
        if (projectFiles is null || projectPath is null || Project is null) return;
        var json = ProjectDataSerializer.Serialize(Project);
        var previous = saveTail;
        saveTail = SaveSnapshotAsync(previous, projectPath, json);
    }

    private void RefreshAvailableSamples(IReadOnlyList<string> choices)
    {
        for (var index = 0; index < choices.Count; index++)
        {
            if (index < availableSamples.Count && availableSamples[index] == choices[index]) continue;
            var existing = availableSamples.IndexOf(choices[index]);
            if (existing >= 0) availableSamples.Move(existing, index);
            else availableSamples.Insert(index, choices[index]);
        }
        while (availableSamples.Count > choices.Count) availableSamples.RemoveAt(availableSamples.Count - 1);
        foreach (var track in tracks)
            track.SelectedSampleFileName ??= track.Samples.FirstOrDefault()?.FileName ?? availableSamples.FirstOrDefault();
        OnPropertyChanged(nameof(CanAddTrack));
        AddSampleTrackCommand.NotifyCanExecuteChanged();
        AddPianoTrackCommand.NotifyCanExecuteChanged();
    }

    private async Task SaveSnapshotAsync(Task previous, string path, string json)
    {
        await previous;
        try
        {
            await projectFiles!.SaveAsync(path, json, CancellationToken.None);
            lastSaveError = null;
            SaveError = null;
        }
        catch (Exception error)
        {
            lastSaveError = error;
            SaveError = error.Message;
        }
    }

    private async Task LoadCoreAsync(string projectPath, int generation, CancellationToken token)
    {
        await CommitAsync(generation, token, () =>
        {
            IsLoading = true;
            ErrorMessage = null;
            playbackErrorMessage = null;
            Waveform = null;
            Project = null;
            markerClock = new([], []);
            ResetMarker(TimeSpan.Zero);
            Position = TimeSpan.Zero;
            IsPlaying = false;
            sampleEventIds.Clear();
            nextSampleEventId = 1;
            NotifyCommandState();
        });
        try
        {
            var source = await sourceService.LoadAsync(projectPath, token);
            var waveform = await audioService.AnalyzeAsync(source.AudioPath, token);
            token.ThrowIfCancellationRequested();
            await CommitAsync(generation, token, () =>
            {
                audioService.Open(source.AudioPath);
                audioService.SetSongVolume(SongVolume);
                audioService.SetApplicationVolume(ApplicationVolume);
                audioService.SetSamplesVolume(SamplesVolume);
                Project = source.Project;
                markerClock = new PlaybackMarkerClock(Project.Offsets, Project.BPMs);
                SetProperty(ref playbackInterval, markerClock.IsValid ? markerClock.GetInterval(TimeSpan.Zero) : TimeSpan.Zero,
                    nameof(PlaybackInterval));
                if (!markerClock.IsValid)
                    ErrorMessage = "Playback requires valid BPM timing data. Reload a beatmap with timing points.";
                Waveform = waveform;
                tracks.Clear();
                foreach (var track in source.Project.Tracks)
                {
                    track.SelectedSampleFileName ??= track.Samples.FirstOrDefault()?.FileName;
                    tracks.Add(track);
                }
                if (messenger is not null)
                {
                    messenger.UnregisterAll(this);
                    messenger.Register<AddSampleTrackRequestedMessage>(this, (_, _) => AddSampleTrackCommand.Execute(null));
                    messenger.Register<AddPianoTrackRequestedMessage>(this, (_, _) => AddPianoTrackCommand.Execute(null));
                    messenger.Register<LoadSampleRequestedMessage>(this, (_, _) => LoadSampleCommand.Execute(null));
                }
            });
            await UpdatePlaybackAsync(projectPath, generation, token);
            if (audioService.GetSnapshot().Error?.StartsWith("Unable to prepare audio:", StringComparison.Ordinal) == true)
                await CommitAsync(generation, CancellationToken.None, () => Waveform = null);
            if (sampleFiles is not null)
            {
                var choices = await sampleFiles.ListAsync(projectPath, token);
                await CommitAsync(generation, token, () =>
                {
                    RefreshAvailableSamples(choices);
                });
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception error)
        {
            await CommitAsync(generation, CancellationToken.None, () => ErrorMessage = error.Message);
        }
        finally
        {
            await CommitAsync(generation, CancellationToken.None, () =>
            {
                IsLoading = false;
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

    private bool CanUseTransport() => CanPlay;
    private bool CanZoomIn() => CanZoom && ZoomFactor < 32;
    private bool CanZoomOut() => CanZoom && ZoomFactor > 16;
    private bool CanIncreaseTrackHeight() => CanZoom && TrackHeight < 288;
    private bool CanDecreaseTrackHeight() => CanZoom && TrackHeight > 36;
    private bool CanIncreaseSubdivision() => SelectedSubdivisionIndex < 8;
    private bool CanDecreaseSubdivision() => SelectedSubdivisionIndex > 0;

    private void SetZoom(int value)
    {
        if (!CanZoom) return;
        var next = Math.Clamp(value, 16, 32);
        if (Math.Abs(zoomFactor - next) > double.Epsilon)
        {
            OnPropertyChanging(nameof(ZoomFactor));
            zoomFactor = next;
            OnPropertyChanged(nameof(ZoomFactor));
        }
        NotifyCommandState();
    }

    private void SetTrackHeight(double value)
    {
        if (!CanZoom) return;
        if (SetProperty(ref trackHeight, Math.Clamp(value, 36, 288), nameof(TrackHeight)))
            NotifyCommandState();
    }

    private void NotifyCommandState()
    {
        PlayPauseCommand.NotifyCanExecuteChanged();
        StopCommand.NotifyCanExecuteChanged();
        SeekCommand.NotifyCanExecuteChanged();
        NextGridLineCommand.NotifyCanExecuteChanged();
        PreviousGridLineCommand.NotifyCanExecuteChanged();
        NextWholeBeatCommand.NotifyCanExecuteChanged();
        PreviousWholeBeatCommand.NotifyCanExecuteChanged();
        ZoomInCommand.NotifyCanExecuteChanged();
        ZoomOutCommand.NotifyCanExecuteChanged();
        IncreaseTrackHeightCommand.NotifyCanExecuteChanged();
        DecreaseTrackHeightCommand.NotifyCanExecuteChanged();
        AddSampleTrackCommand.NotifyCanExecuteChanged();
        AddPianoTrackCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(CanPlay));
        OnPropertyChanged(nameof(CanZoom));
        OnPropertyChanged(nameof(CanPlaceSamples));
    }

    private void NotifyEditingState()
    {
        OnPropertyChanged(nameof(CanImportSample));
        OnPropertyChanged(nameof(CanAddTrack));
        OnPropertyChanged(nameof(CanRemoveTrack));
        OnPropertyChanged(nameof(CanPlaceSamples));
        LoadSampleCommand.NotifyCanExecuteChanged();
        AddSampleTrackCommand.NotifyCanExecuteChanged();
        AddPianoTrackCommand.NotifyCanExecuteChanged();
    }
}
