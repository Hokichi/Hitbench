using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using osuHitsounder.Core.Entities;
using osuHitsounder.Services;
using osuHitsounder.Services.FileSystem;
using osuHitsounder.Services.Ui;
using System.Globalization;

namespace osuHitsounder.ViewModels.Shell;

public partial class WorkstationPageVM(
    IWorkstationSourceService sourceService,
    IWorkstationAudioService audioService,
    IUiScheduler uiScheduler) : ObservableObject
{
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
    private double zoomFactor = 1;
    private int selectedSubdivisionIndex = 3;
    [ObservableProperty] private string zoomDraft = "1";
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasZoomValidationError))]
    private string? zoomValidationMessage;

    public double ZoomFactor => zoomFactor;
    public string ZoomText => $"{ZoomFactor:0.#}x";
    public bool HasZoomValidationError => ZoomValidationMessage is not null;
    public int SelectedSubdivisionIndex
    {
        get => selectedSubdivisionIndex;
        set
        {
            var next = Math.Clamp(value, 0, 8);
            if (!SetProperty(ref selectedSubdivisionIndex, next)) return;
            IncreaseSubdivisionCommand.NotifyCanExecuteChanged();
            DecreaseSubdivisionCommand.NotifyCanExecuteChanged();
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
        audioService.Stop();
        audioService.Dispose();
        loadCancellation?.Dispose();
        loadCancellation = null;
        NotifyCommandState();
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

    [RelayCommand(CanExecute = nameof(CanUseZoom))]
    private void Fit()
    {
        if (!CanUseZoom()) return;
        SetZoom(1);
    }

    [RelayCommand(CanExecute = nameof(CanUseZoom))]
    private void CommitZoom()
    {
        if (!CanUseZoom()) return;
        if (!int.TryParse(ZoomDraft, NumberStyles.Integer, CultureInfo.CurrentCulture, out var value) || value is < 1 or > 16)
        {
            ZoomValidationMessage = "Enter a whole number from 1 to 16.";
            return;
        }
        SetZoom(value);
    }

    [RelayCommand]
    private void CancelZoomEdit() => ResetZoomDraft();

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
    private static void AddSampleTrack() { }

    [RelayCommand]
    private static void AddPianoTrack() { }

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
            });
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
    private bool CanUseZoom() => CanZoom;
    private bool CanZoomIn() => CanZoom && ZoomFactor < 16;
    private bool CanZoomOut() => CanZoom && ZoomFactor > 1;
    private bool CanIncreaseSubdivision() => SelectedSubdivisionIndex < 8;
    private bool CanDecreaseSubdivision() => SelectedSubdivisionIndex > 0;

    private void SetZoom(int value)
    {
        if (!CanZoom) return;
        var next = Math.Clamp(value, 1, 16);
        if (Math.Abs(zoomFactor - next) > double.Epsilon)
        {
            OnPropertyChanging(nameof(ZoomFactor));
            zoomFactor = next;
            OnPropertyChanged(nameof(ZoomFactor));
            OnPropertyChanged(nameof(ZoomText));
        }
        ResetZoomDraft();
        NotifyCommandState();
    }

    private void ResetZoomDraft()
    {
        ZoomDraft = ((int)ZoomFactor).ToString(CultureInfo.CurrentCulture);
        ZoomValidationMessage = null;
    }

    private void NotifyCommandState()
    {
        PlayPauseCommand.NotifyCanExecuteChanged();
        StopCommand.NotifyCanExecuteChanged();
        SeekCommand.NotifyCanExecuteChanged();
        ZoomInCommand.NotifyCanExecuteChanged();
        ZoomOutCommand.NotifyCanExecuteChanged();
        FitCommand.NotifyCanExecuteChanged();
        CommitZoomCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(CanPlay));
        OnPropertyChanged(nameof(CanZoom));
        if (!CanZoom) ResetZoomDraft();
    }
}
