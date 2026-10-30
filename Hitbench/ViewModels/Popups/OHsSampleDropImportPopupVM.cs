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
        var entered = false;
        try { await reads.WaitAsync(lifetime.Token); entered = true; var data = await browser.ReadWaveformAsync(entry.SourcePath, lifetime.Token); await scheduler.InvokeAsync(() => { if (!closed && !lifetime.IsCancellationRequested) entry.Waveform = data; }, lifetime.Token); }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
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
        try
        {
            await previous; await preview.StopAsync(CancellationToken.None); if (closed || generation != previewGeneration || !play) return;
            await preview.PlayAsync(entry.SourcePath, volumes?.ApplicationVolume ?? 100, lifetime.Token);
            if (closed || generation != previewGeneration) { await preview.StopAsync(CancellationToken.None); return; }
            monitors.Add(MonitorAsync(entry, generation));
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        catch (Exception e) { await scheduler.InvokeAsync(() => { if (!closed && generation == previewGeneration) entry.IsPlaying = false; }, CancellationToken.None); System.Diagnostics.Trace.TraceWarning("Dropped sample preview failed: {0}", e.Message); }
    }
    private async Task MonitorAsync(OHsDroppedSampleEntry entry, int generation)
    {
        try { while (!closed && generation == previewGeneration) { await Task.Delay(50, lifetime.Token); var state = await preview.GetSnapshotAsync(lifetime.Token); await scheduler.InvokeAsync(() => { if (!closed && generation == previewGeneration) entry.IsPlaying = state.IsPlaying; }, lifetime.Token); if (!state.IsPlaying) return; } }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        catch (Exception e) { System.Diagnostics.Trace.TraceWarning("Dropped sample preview monitor failed: {0}", e.Message); }
    }
    public async Task DeactivateAsync()
    {
        if (closed) return; closed = true; ++previewGeneration; lifetime.Cancel(); OnPropertyChanged(nameof(CanConfirm)); PlayCommand.NotifyCanExecuteChanged();
        foreach (var entry in Entries) entry.PropertyChanged -= OnEntryChanged;
        await Task.WhenAll(waveforms.Values); await playback; await Task.WhenAll(monitors);
        try { await preview.StopAsync(CancellationToken.None); } finally { await preview.DisposeAsync(); if (browser is IDisposable disposable) disposable.Dispose(); reads.Dispose(); lifetime.Dispose(); }
    }
}
