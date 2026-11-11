using Hitbench.Resources.DataModels;
using Hitbench.Services.FileSystem;

namespace Hitbench.ViewModels.Popups;

public partial class OHsHitNormalPopupVM
{
    private CancellationTokenSource lifetime = new();
    private readonly SemaphoreSlim waveformGate = new(2, 2);
    private readonly Dictionary<OHsHitNormalSample, Task> waveformLoads = [];
    private bool initialized;
    private bool deactivated;
    private Task? deactivation;

    public Task InitializeAsync(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (deactivated) throw new InvalidOperationException("Hitnormal dialog has closed.");
        if (!initialized)
        {
            lifetime.Dispose();
            lifetime = CancellationTokenSource.CreateLinkedTokenSource(token);
            initialized = true;
        }
        return Task.CompletedTask;
    }

    public Task LoadWaveformAsync(OHsHitNormalSample sample)
    {
        if (deactivated || lifetime.IsCancellationRequested || waveformReader is null || sample.Path.Length == 0 ||
            !samples.Contains(sample) || sample.Waveform is not null) return Task.CompletedTask;
        if (waveformLoads.TryGetValue(sample, out var loading)) return loading;
        var task = ReadWaveformAsync(sample, lifetime.Token);
        waveformLoads.Add(sample, task);
        return task;
    }

    private async Task ReadWaveformAsync(OHsHitNormalSample sample, CancellationToken token)
    {
        var entered = false;
        try
        {
            await waveformGate.WaitAsync(token); entered = true;
            token.ThrowIfCancellationRequested();
            var path = OHsHitNormalSamples.Resolve(projectPath!, mapsetDirectory ?? "", sample.Path);
            var waveform = await waveformReader!.ReadWaveformAsync(path, token);
            await scheduler!.InvokeAsync(() =>
            {
                if (!deactivated && !token.IsCancellationRequested) sample.Waveform = waveform;
            }, token);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception error) { System.Diagnostics.Trace.TraceWarning("Hitnormal waveform unavailable: {0}", error.Message); }
        finally { if (entered) waveformGate.Release(); }
    }

    public Task DeactivateAsync() => deactivation ??= StopWaveformsAsync();

    private async Task StopWaveformsAsync()
    {
        deactivated = true;
        lifetime.Cancel();
        AssignSampleCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(CanSave));
        try { await Task.WhenAll(waveformLoads.Values); }
        finally { waveformGate.Dispose(); lifetime.Dispose(); }
    }
}
