using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using osuHitsounder.Core.Entities;
using osuHitsounder.Services;
using osuHitsounder.Services.FileSystem;
using osuHitsounder.Services.Ui;

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
    [ObservableProperty] private TimeSpan position;
    [ObservableProperty] private string elapsedText = "00:00.0";
    [ObservableProperty] private bool isLoading;
    [ObservableProperty] private bool isPlaying;
    [ObservableProperty] private string? errorMessage;
    [ObservableProperty] private double zoomFactor = 1;

    public string ZoomText => $"{ZoomFactor:0.#}x";
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

    [RelayCommand(CanExecute = nameof(CanZoomIn))]
    private void ZoomIn()
    {
        ZoomFactor = Math.Min(16, ZoomFactor * 2);
        OnPropertyChanged(nameof(ZoomText));
        NotifyCommandState();
    }

    [RelayCommand(CanExecute = nameof(CanZoomOut))]
    private void ZoomOut()
    {
        ZoomFactor = Math.Max(1, ZoomFactor / 2);
        OnPropertyChanged(nameof(ZoomText));
        NotifyCommandState();
    }

    [RelayCommand(CanExecute = nameof(CanUseZoom))]
    private void Fit()
    {
        ZoomFactor = 1;
        OnPropertyChanged(nameof(ZoomText));
        NotifyCommandState();
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

    private void NotifyCommandState()
    {
        PlayPauseCommand.NotifyCanExecuteChanged();
        StopCommand.NotifyCanExecuteChanged();
        ZoomInCommand.NotifyCanExecuteChanged();
        ZoomOutCommand.NotifyCanExecuteChanged();
        FitCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(CanPlay));
        OnPropertyChanged(nameof(CanZoom));
    }
}
