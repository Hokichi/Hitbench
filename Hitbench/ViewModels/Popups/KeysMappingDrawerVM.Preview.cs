using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Hitbench.Services;
using Hitbench.Services.FileSystem;
namespace Hitbench.ViewModels.Popups;
public sealed partial class KeysMappingDrawerVM
{
    private readonly ISamplePreviewService? preview;
    private readonly PlaybackVolumeSettings? playbackVolumes;
    private CancellationTokenSource? previewCancellation;
    private Task previewOperation = Task.CompletedTask, previewMonitor = Task.CompletedTask;
    private Task snapshotPreviewStop = Task.CompletedTask;
    private int previewGeneration;
    private readonly List<CancellationTokenSource> previewSources = [];
    [ObservableProperty] private string? previewWarning;
    public bool CanPlaySelected => CanEditSelectedMapping && preview is not null && SelectedEntry is { FileName: not null, IsMissing: false };
    [RelayCommand(CanExecute = nameof(CanPlaySelected), AllowConcurrentExecutions = true)]
    public async Task PlaySelectedAsync(CancellationToken token)
    {
        if (token.IsCancellationRequested || !CanPlaySelected || SelectedEntry is not { } entry) return;
        if (!await CommitMappingAsync(entry.Key, entry.DraftFileName, token) || !CanPlaySelected) return;
        var generation = ++previewGeneration;
        previewCancellation?.Cancel();
        var retiredSources = previewSources.ToArray();
        var cancel = CancellationTokenSource.CreateLinkedTokenSource(token, lifetime.Token);
        previewCancellation = cancel; previewSources.Add(cancel);
        var previous = previewOperation;
        previewOperation = StartPreviewAsync(previous, entry.FileName!, generation, cancel, retiredSources);
        await previewOperation;
    }
    private async Task StartPreviewAsync(Task previous, string path, int generation, CancellationTokenSource cancel, CancellationTokenSource[] retiredSources)
    {
        try
        {
            await previous; await previewMonitor;
            foreach (var retired in retiredSources)
            { retired.Dispose(); previewSources.Remove(retired); }
            if (generation != previewGeneration || cancel.IsCancellationRequested) return;
            await preview!.StopAsync(CancellationToken.None);
            if (generation != previewGeneration || cancel.IsCancellationRequested) return;
            await preview.PlayAsync(SamplePaths.Resolve(snapshot.Context.ProjectPath,path),playbackVolumes?.ApplicationVolume ?? 100,cancel.Token);
            if (generation != previewGeneration || cancel.IsCancellationRequested) { await preview.StopAsync(CancellationToken.None); return; }
            previewMonitor = MonitorPreviewAsync(generation, cancel.Token);
        }
        catch (OperationCanceledException) when (cancel.IsCancellationRequested) { }
        catch (Exception error) { await ReportPreviewErrorAsync(error, generation); }
    }
    private async Task MonitorPreviewAsync(int generation, CancellationToken token)
    {
        try
        {
            while (!token.IsCancellationRequested)
            {
                var state = await preview!.GetSnapshotAsync(token);
                await scheduler.InvokeAsync(() => { if (!closed && !closing && generation == previewGeneration) { PreviewWarning = state.Warning; if (state.Error is not null) ErrorMessage = state.Error; } },token);
                if (!state.IsPlaying) return;
                await Task.Delay(50,token);
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception error) { await ReportPreviewErrorAsync(error, generation); }
    }
    private Task ReportPreviewErrorAsync(Exception error, int generation) => scheduler.InvokeAsync(() =>
    {
        if (!closing && !closed && generation == previewGeneration) ErrorMessage = error.Message;
    }, CancellationToken.None);
    private Task StopPreviewAsync()
    {
        ++previewGeneration; previewCancellation?.Cancel();
        var previous = previewOperation;
        var sources = previewSources.ToArray();
        previewOperation = StopPreviewCoreAsync(previous, sources);
        return previewOperation;
    }
    private async Task StopPreviewCoreAsync(Task previous, CancellationTokenSource[] sources)
    {
        try
        {
            await previous; await previewMonitor;
            if (preview is not null) await preview.StopAsync(CancellationToken.None);
        }
        catch (Exception error)
        {
            await scheduler.InvokeAsync(() => { if (!closed) ErrorMessage = error.Message; }, CancellationToken.None);
        }
        finally
        {
            foreach (var source in sources)
            {
                source.Dispose(); previewSources.Remove(source);
                if (ReferenceEquals(previewCancellation, source)) previewCancellation = null;
            }
        }
    }
}
