using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using osuHitsounder.Core.Entities;
using osuHitsounder.Resources.DataModels;

namespace osuHitsounder.ViewModels.Popups;

public sealed partial class SampleLoaderPopupVM
{
    private int stoppedSelectionGeneration = -1;
    [ObservableProperty] private WaveformData? waveform;
    public TimeSpan PlaybackPosition => TimeSpan.FromSeconds(PlaybackPositionSeconds);
    public bool CanPlayPreview => CanPreview && !IsPreviewPlaying;
    public bool CanStopPreview => !deactivated && !IsConfirming && SelectedFile is not null && !isStartingPreview;
    [RelayCommand(CanExecute = nameof(CanPlayPreview))]
    private Task PlayPreviewAsync() => OwnAsync(StartPreviewAsync);
    [RelayCommand(CanExecute = nameof(CanStopPreview))]
    private Task StopPreviewAsync() => OwnAsync(async () =>
    {
        stoppedSelectionGeneration = selectionGeneration;
        try { await StopPreviewCoreAsync(); }
        catch (Exception error) { await ReportAsync(() => PreviewError = error.Message); }
    });
    [RelayCommand(CanExecute = nameof(CanSeek))]
    private Task SeekPreviewAsync(TimeSpan position) => SeekAsync(position.TotalSeconds);
    private async Task LoadWaveformAsync(SampleBrowserEntry file, int generation, CancellationToken token)
    {
        try
        {
            var result = await browser.ReadWaveformAsync(file.FullPath, token);
            if (!deactivated && generation == selectionGeneration && !token.IsCancellationRequested)
                await UpdateAsync(() => { if (generation == selectionGeneration && !token.IsCancellationRequested) Waveform = result; });
        }
        catch (OperationCanceledException) { }
        catch (Exception error) { System.Diagnostics.Trace.TraceWarning("Sample waveform unavailable: {0}", error.Message); }
    }
    [RelayCommand(CanExecute = nameof(CanPreview))]
    private Task TogglePreviewAsync() => OwnAsync(async () =>
    {
        if (IsPreviewPlaying) { await StopPreviewCoreAsync(); return; }
        await StartPreviewAsync();
    });

    private async Task StartPreviewAsync()
    {
        if (!CanPreview || SelectedFile is not { } file) return;
        playCancellation?.Cancel();
        playCancellation?.Dispose();
        var cancellation = playCancellation = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        var request = selectionGeneration;
        var entered = false;
        try
        {
            await UpdateAsync(() => { isStartingPreview = true; PreviewError = null; PreviewWarning = null; NotifyState(); });
            await previewGate.WaitAsync(cancellation.Token);
            entered = true;
            if (request != selectionGeneration || deactivated) return;
            var start = PlaybackPositionSeconds >= PlaybackDurationSeconds ? 0 : PlaybackPositionSeconds;
            await preview.PlayAsync(file.FullPath, volumes.ApplicationVolume, cancellation.Token, TimeSpan.FromSeconds(start));
            if (request != selectionGeneration || deactivated || cancellation.IsCancellationRequested) { await preview.StopAsync(CancellationToken.None); return; }
            await UpdateAsync(() => { playbackPositionSeconds = start; IsPreviewPlaying = true; NotifyState(); });
            previewMonitor = MonitorPreviewAsync(request, cancellation.Token);
        }
        catch (OperationCanceledException) { }
        catch (Exception error)
        {
            if (request == selectionGeneration) await ReportAsync(() => { PreviewError = error.Message; IsPreviewPlaying = false; NotifyState(); });
        }
        finally
        {
            await ReportAsync(() => { isStartingPreview = false; NotifyState(); });
            if (entered) previewGate.Release();
        }
    }

    public Task SeekAsync(double seconds) => OwnAsync(async () =>
    {
        if (!CanSeek || !double.IsFinite(seconds)) return;
        var request = selectionGeneration;
        var target = Math.Clamp(seconds, 0, PlaybackDurationSeconds);
        if (!IsPreviewPlaying)
        {
            await UpdateAsync(() => { playbackPositionSeconds = target; NotifyState(); });
            return;
        }
        var entered = false;
        var wasPlaying = true;
        playCancellation?.Cancel();
        playCancellation?.Dispose();
        var cancellation = playCancellation = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        try
        {
            await UpdateAsync(() => { isSeeking = true; PreviewError = null; NotifyState(); });
            await previewGate.WaitAsync(cancellation.Token);
            entered = true;
            await previewMonitor;
            if (request != selectionGeneration || deactivated) return;
            await preview.SeekAsync(TimeSpan.FromSeconds(target), cancellation.Token);
            if (request != selectionGeneration || deactivated || cancellation.IsCancellationRequested) return;
            await UpdateAsync(() => { playbackPositionSeconds = target; NotifyState(); });
        }
        catch (OperationCanceledException) { wasPlaying = false; }
        catch (Exception error)
        {
            wasPlaying = false;
            if (request == selectionGeneration) await ReportAsync(() => { PreviewError = error.Message; IsPreviewPlaying = false; NotifyState(); });
        }
        finally
        {
            await ReportAsync(() => { isSeeking = false; NotifyState(); });
            if (entered) previewGate.Release();
            if (wasPlaying && request == selectionGeneration && !deactivated && !cancellation.IsCancellationRequested)
                previewMonitor = MonitorPreviewAsync(request, cancellation.Token);
        }
    });

    private async Task MonitorPreviewAsync(int request, CancellationToken token)
    {
        try
        {
            while (!token.IsCancellationRequested)
            {
                var state = await preview.GetSnapshotAsync(token);
                if (request != selectionGeneration || deactivated) return;
                if (token.IsCancellationRequested) return;
                await UpdateAsync(() =>
                {
                    if (request != selectionGeneration || token.IsCancellationRequested) return;
                    playbackPositionSeconds = Math.Clamp(state.Position.TotalSeconds, 0, PlaybackDurationSeconds);
                    IsPreviewPlaying = state.IsPlaying; PreviewError = state.Error; PreviewWarning = state.Warning; NotifyState();
                });
                if (!state.IsPlaying) return;
                await Task.Delay(100, token);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception error) { await ReportAsync(() => { PreviewError = error.Message; IsPreviewPlaying = false; NotifyState(); }); }
    }

    private async Task StopPreviewCoreAsync()
    {
        playCancellation?.Cancel();
        await previewMonitor;
        await previewGate.WaitAsync();
        try { await preview.StopAsync(CancellationToken.None); }
        finally { previewGate.Release(); }
        await ReportAsync(() => { playbackPositionSeconds = 0; IsPreviewPlaying = false; PreviewError = null; PreviewWarning = null; NotifyState(); });
    }

}
