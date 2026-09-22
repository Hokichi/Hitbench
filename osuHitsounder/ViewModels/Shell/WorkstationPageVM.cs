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
    IMessenger? messenger = null) : ObservableObject
{
    private readonly ObservableCollection<Track> tracks = [];
    private readonly ObservableCollection<string> availableSamples = [];
    private string? projectPath;
    private Task saveTail = Task.CompletedTask;
    private Task<string?>? activeImport;
    private Exception? lastSaveError;
    private bool importBusy;
    public ObservableCollection<Track> Tracks => tracks;
    public ObservableCollection<string> SampleFiles => availableSamples;
    public int Subdivision => new[] { 1, 2, 3, 4, 5, 6, 8, 12, 16 }[SelectedSubdivisionIndex];
    public bool CanEditTracks => Project is not null && !IsLoading && !deactivated;
    public bool CanImportSample => CanEditTracks && !importBusy;
    public bool HasPendingSaves => importBusy || !saveTail.IsCompleted || lastSaveError is not null;
    [ObservableProperty] private string? saveError;
    private CancellationTokenSource? loadCancellation;
    private Task? activeLoad;
    private int loadGeneration;
    private bool deactivated;

    [ObservableProperty] private WaveformData? waveform;
    [ObservableProperty] private ProjectData? project;
    [ObservableProperty] private TimeSpan position;
    [ObservableProperty] private string elapsedText = "00:00.0";
    [ObservableProperty] private bool isLoading;
    [ObservableProperty] private bool isPlaying;
    [ObservableProperty] private string? errorMessage;
    private double zoomFactor = 8;
    private int selectedSubdivisionIndex = 3;

    public double ZoomFactor => zoomFactor;
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
        loadGeneration++;
        loadCancellation?.Cancel();
        if (activeLoad is not null)
        {
            try { await activeLoad; }
            catch (OperationCanceledException) { }
        }
        if (messenger is not null) messenger.UnregisterAll(this);
        try { await FlushSavesAsync(); }
        finally
        {
            audioService.Stop();
            audioService.Dispose();
            loadCancellation?.Dispose();
            loadCancellation = null;
            NotifyCommandState();
        }
    }

    public void RefreshPlayback()
    {
        if (deactivated) return;
        var snapshot = audioService.GetSnapshot();
        Position = snapshot.Position;
        IsPlaying = snapshot.IsPlaying;
        ElapsedText = $"{(int)snapshot.Position.TotalMinutes:00}:{snapshot.Position.Seconds:00}.{snapshot.Position.Milliseconds / 100}";
        if (!string.IsNullOrWhiteSpace(snapshot.Error)) ErrorMessage = snapshot.Error;
    }

    [RelayCommand(CanExecute = nameof(CanUseTransport))]
    private void PlayPause()
    {
        if (audioService.GetSnapshot().IsPlaying) audioService.Pause();
        else audioService.Play();
        RefreshPlayback();
    }

    [RelayCommand(CanExecute = nameof(CanUseTransport))]
    private void Stop()
    {
        audioService.Stop();
        RefreshPlayback();
    }

    [RelayCommand(CanExecute = nameof(CanUseTransport))]
    private void Seek(TimeSpan position)
    {
        if (!CanUseTransport()) return;
        audioService.Seek(position);
        RefreshPlayback();
    }

    [RelayCommand(CanExecute = nameof(CanUseTransport))]
    private void NextGridLine() => SeekAdjacentGridLine(true);

    [RelayCommand(CanExecute = nameof(CanUseTransport))]
    private void PreviousGridLine() => SeekAdjacentGridLine(false);

    private void SeekAdjacentGridLine(bool forward)
    {
        if (!CanUseTransport() || Project is null || Waveform is null) return;
        var position = audioService.GetSnapshot().Position;
        var target = BeatGridCalculator.GetAdjacentPosition(Project.Offsets, Project.BPMs,
            Subdivision, position, Waveform.Duration, forward);
        if (target is not null) Seek(target.Value);
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

    [RelayCommand]
    private void AddSampleTrack()
    {
        if (!CanEditTracks) return;
        var track = new Track();
        if (availableSamples.Count > 0) track.Samples.Add(new Sample { FileName = availableSamples[0] });
        Project!.Tracks.Add(track);
        tracks.Add(track);
        QueueSave();
    }

    [RelayCommand]
    private static void AddPianoTrack() { }

    public void SelectSample(Track track, string fileName)
    {
        if (!CanEditTracks || !tracks.Contains(track) || string.IsNullOrWhiteSpace(fileName) ||
            sampleFiles is not null && !availableSamples.Any(name => string.Equals(name, fileName, StringComparison.OrdinalIgnoreCase))) return;
        if (track.Samples.Count == 0) track.Samples.Add(new Sample { FileName = fileName });
        else foreach (var sample in track.Samples) sample.FileName = fileName;
        QueueSave();
    }

    public void SetTrackVolume(Track track, int volume)
    {
        if (!CanEditTracks || !tracks.Contains(track)) return;
        var next = Math.Clamp(volume, 5, 100);
        if (track.Volume == next) return;
        track.Volume = next;
        QueueSave();
    }

    public Task<string?> ImportSampleAsync(Track track)
    {
        if (!CanEditTracks || !tracks.Contains(track) || sampleDialog is null || sampleFiles is null || importBusy)
            return Task.FromResult<string?>(null);
        importBusy = true;
        OnPropertyChanged(nameof(CanImportSample));
        activeImport = ImportSampleCoreAsync(track);
        return activeImport;
    }

    private async Task<string?> ImportSampleCoreAsync(Track track)
    {
        try
        {
            var source = await sampleDialog!.SelectAsync(loadCancellation?.Token ?? CancellationToken.None);
            if (source is null || !tracks.Contains(track) || deactivated) return null;
            var name = await sampleFiles!.ImportAsync(projectPath!, source, loadCancellation?.Token ?? CancellationToken.None);
            var choices = await sampleFiles.ListAsync(projectPath!, loadCancellation?.Token ?? CancellationToken.None);
            RefreshAvailableSamples(choices);
            SelectSample(track, name);
            return name;
        }
        catch (OperationCanceledException) { return null; }
        catch (Exception error) { ErrorMessage = error.Message; return null; }
        finally
        {
            importBusy = false;
            OnPropertyChanged(nameof(CanImportSample));
        }
    }

    public async Task FlushSavesAsync()
    {
        if (activeImport is not null) await activeImport;
        await saveTail;
        if (lastSaveError is not null) throw new IOException("Could not save project.", lastSaveError);
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
            Waveform = null;
            Project = null;
            Position = TimeSpan.Zero;
            IsPlaying = false;
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
                Project = source.Project;
                Waveform = waveform;
                tracks.Clear();
                foreach (var track in source.Project.Tracks) tracks.Add(track);
                if (messenger is not null)
                {
                    messenger.UnregisterAll(this);
                    messenger.Register<AddSampleTrackRequestedMessage>(this, (_, _) => AddSampleTrackCommand.Execute(null));
                    messenger.Register<AddPianoTrackRequestedMessage>(this, (_, _) => AddPianoTrackCommand.Execute(null));
                }
            });
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
                OnPropertyChanged(nameof(CanImportSample));
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
    private bool CanZoomOut() => CanZoom && ZoomFactor > 8;
    private bool CanIncreaseSubdivision() => SelectedSubdivisionIndex < 8;
    private bool CanDecreaseSubdivision() => SelectedSubdivisionIndex > 0;

    private void SetZoom(int value)
    {
        if (!CanZoom) return;
        var next = Math.Clamp(value, 8, 32);
        if (Math.Abs(zoomFactor - next) > double.Epsilon)
        {
            OnPropertyChanging(nameof(ZoomFactor));
            zoomFactor = next;
            OnPropertyChanged(nameof(ZoomFactor));
        }
        NotifyCommandState();
    }

    private void NotifyCommandState()
    {
        PlayPauseCommand.NotifyCanExecuteChanged();
        StopCommand.NotifyCanExecuteChanged();
        SeekCommand.NotifyCanExecuteChanged();
        NextGridLineCommand.NotifyCanExecuteChanged();
        PreviousGridLineCommand.NotifyCanExecuteChanged();
        ZoomInCommand.NotifyCanExecuteChanged();
        ZoomOutCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(CanPlay));
        OnPropertyChanged(nameof(CanZoom));
    }
}
