using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Hitbench.Resources.DataModels;
using Hitbench.Services;
using Hitbench.Services.FileSystem;
using Hitbench.Services.Ui;
using System.Collections.ObjectModel;
namespace Hitbench.ViewModels.Popups;

public sealed partial class OHsSampleDropImportPopupVM : ObservableObject
{
    private readonly ISampleBrowserService browser;
    private readonly ISamplePreviewService preview;
    private readonly IUiScheduler scheduler;
    private readonly PlaybackVolumeSettings? volumes;
    private CancellationTokenSource lifetime = new();
    private readonly SemaphoreSlim reads = new(2);
    private readonly Dictionary<OHsDroppedSampleEntry, Task> waveforms = [];
    private Task playback = Task.CompletedTask;
    private readonly List<Task> monitors = [];
    private int previewGeneration;
    private bool closed;
    private Task? deactivation;
    public bool? SelectionAccepted { get; private set; }
    public IReadOnlyList<string> SelectedPaths { get; private set; } = Array.Empty<string>();
    public bool TryCompleteSelection(bool accepted)
    {
        if (SelectionAccepted.HasValue || closed || (accepted && !CanConfirm)) return false;
        SelectedPaths = accepted ? GetCheckedPaths() : Array.Empty<string>();
        SelectionAccepted = accepted;
        RequestCancellation();
        return true;
    }
    public void RequestCancellation()
    {
        if (closed) return;
        closed = true; ++previewGeneration;
        foreach (var entry in Entries) { entry.IsPlaying = false; entry.PropertyChanged -= OnEntryChanged; }
        lifetime.Cancel();
        OnPropertyChanged(nameof(CanConfirm)); PlayCommand.NotifyCanExecuteChanged();
    }
    public ObservableCollection<OHsDroppedSampleEntry> Entries { get; }
    public bool CanConfirm => !closed && Entries.Any(e => e.IsChecked);
    public OHsSampleDropImportPopupVM(IReadOnlyList<string> paths, ISampleBrowserService browser, ISamplePreviewService preview, IUiScheduler scheduler, PlaybackVolumeSettings? playbackVolumes = null)
    {
        this.browser = browser; this.preview = preview; this.scheduler = scheduler; volumes = playbackVolumes;
        Entries = new(paths.Select(p => new OHsDroppedSampleEntry(p)));
        foreach (var entry in Entries) entry.PropertyChanged += OnEntryChanged;
    }
    private void OnEntryChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e) { if (e.PropertyName == nameof(OHsDroppedSampleEntry.IsChecked)) OnPropertyChanged(nameof(CanConfirm)); }
    public Task InitializeAsync(CancellationToken token) { lifetime.Dispose(); lifetime = CancellationTokenSource.CreateLinkedTokenSource(token); return Task.CompletedTask; }
    public IReadOnlyList<string> GetCheckedPaths() => CanConfirm ? Array.AsReadOnly(Entries.Where(e => e.IsChecked).Select(e => e.SourcePath).ToArray()) : Array.Empty<string>();
    public Task LoadWaveformAsync(OHsDroppedSampleEntry entry)
    {
        if (closed || !Entries.Contains(entry)) return Task.CompletedTask;
        if (waveforms.TryGetValue(entry, out var existing)) return existing;
        var task = ReadAsync(entry); waveforms.Add(entry, task); return task;
    }
    private async Task ReadAsync(OHsDroppedSampleEntry entry)
    {
        var token = lifetime.Token;
        var entered = false;
        try { await reads.WaitAsync(token); entered = true; var data = await browser.ReadWaveformAsync(entry.SourcePath, token); await scheduler.InvokeAsync(() => { if (!closed && !token.IsCancellationRequested) entry.Waveform = data; }, token); }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception e) { System.Diagnostics.Trace.TraceWarning("Dropped sample waveform unavailable: {0}", e.Message); }
        finally { if (entered) reads.Release(); }
    }
    private bool CanPlay(OHsDroppedSampleEntry? entry) => !closed && entry is not null && Entries.Contains(entry);
    [RelayCommand(CanExecute = nameof(CanPlay), AllowConcurrentExecutions = true)]
    private Task PlayAsync(OHsDroppedSampleEntry entry)
    {
        if (!CanPlay(entry)) return Task.CompletedTask;
        var play = !entry.IsPlaying; var generation = ++previewGeneration;
        foreach (var row in Entries) row.IsPlaying = play && ReferenceEquals(row, entry);
        playback = ChangePreviewAsync(playback, entry, play, generation); return playback;
    }
    private async Task ChangePreviewAsync(Task previous, OHsDroppedSampleEntry entry, bool play, int generation)
    {
        var token = lifetime.Token;
        try
        {
            await previous; await preview.StopAsync(CancellationToken.None); if (closed || generation != previewGeneration || !play) return;
            await preview.PlayAsync(entry.SourcePath, volumes?.ApplicationVolume ?? 100, token);
            if (closed || generation != previewGeneration) { await preview.StopAsync(CancellationToken.None); return; }
            monitors.Add(MonitorAsync(entry, generation));
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception e) { await scheduler.InvokeAsync(() => { if (!closed && generation == previewGeneration) entry.IsPlaying = false; }, CancellationToken.None); System.Diagnostics.Trace.TraceWarning("Dropped sample preview failed: {0}", e.Message); }
    }
    private async Task MonitorAsync(OHsDroppedSampleEntry entry, int generation)
    {
        var token = lifetime.Token;
        try { while (!closed && generation == previewGeneration) { await Task.Delay(50, token); var state = await preview.GetSnapshotAsync(token); await scheduler.InvokeAsync(() => { if (!closed && generation == previewGeneration) entry.IsPlaying = state.IsPlaying; }, token); if (!state.IsPlaying) return; } }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception e) { System.Diagnostics.Trace.TraceWarning("Dropped sample preview monitor failed: {0}", e.Message); }
    }
    public Task DeactivateAsync()
    {
        if (deactivation is not null) return deactivation;
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        deactivation = completion.Task;
        _ = CompleteAsync();
        return deactivation;
        async Task CompleteAsync()
        {
            try { await DeactivateCoreAsync(); completion.SetResult(); }
            catch (Exception error) { completion.SetException(error); }
        }
    }
    private async Task DeactivateCoreAsync()
    {
        Exception? failure = null;
        void Record(Exception error)
        {
            failure ??= error;
            System.Diagnostics.Trace.TraceWarning("Dropped sample cleanup failed: {0}", error);
        }
        try { RequestCancellation(); } catch (Exception error) { Record(error); }
        try { await Task.WhenAll(waveforms.Values); } catch (Exception error) { Record(error); }
        try { await playback; } catch (Exception error) { Record(error); }
        try { await Task.WhenAll(monitors); } catch (Exception error) { Record(error); }
        try { await preview.StopAsync(CancellationToken.None); } catch (Exception error) { Record(error); }
        try { await preview.DisposeAsync(); } catch (Exception error) { Record(error); }
        try { if (browser is IDisposable disposable) disposable.Dispose(); } catch (Exception error) { Record(error); }
        reads.Dispose(); lifetime.Dispose();
        if (failure is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
