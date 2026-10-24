using Hitbench.Resources.DataModels;
using Hitbench.Services.FileSystem;

namespace Hitbench.ViewModels.Popups;

public sealed partial class ManageSamplesPopupVM
{
    private readonly SemaphoreSlim waveformGate = new(2, 2);
    private readonly Dictionary<SampleManagementEntry, Task> waveformLoads = [];
    private readonly Dictionary<SampleManagementEntry, CancellationTokenSource> waveformTokens = [];
    private readonly List<Task> waveformOperations = [];

    public Task LoadWaveformAsync(SampleManagementEntry entry)
    {
        if (deactivated || waveformReader is null || !Entries.Contains(entry) || entry.Waveform is not null) return Task.CompletedTask;
        if (waveformLoads.TryGetValue(entry, out var pending)) return pending;
        var cancellation = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        waveformTokens[entry] = cancellation;
        var task = ReadRowWaveformAsync(entry, entry.FileName, cancellation);
        waveformLoads[entry] = task; waveformOperations.Add(task);
        return task;
    }

    private async Task ReadRowWaveformAsync(SampleManagementEntry entry, string name, CancellationTokenSource cancellation)
    {
        var entered = false;
        try
        {
            await waveformGate.WaitAsync(cancellation.Token); entered = true;
            var result = await waveformReader!.ReadWaveformAsync(SamplePaths.Resolve(Context.ProjectPath, name), cancellation.Token);
            await scheduler.InvokeAsync(() =>
            {
                if (!deactivated && !cancellation.IsCancellationRequested && Entries.Contains(entry) && entry.FileName == name &&
                    waveformTokens.TryGetValue(entry, out var current) && ReferenceEquals(current, cancellation)) entry.Waveform = result;
            }, cancellation.Token);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        catch (Exception error) { System.Diagnostics.Trace.TraceWarning("Sample waveform unavailable: {0}", error.Message); }
        finally { if (entered) waveformGate.Release(); }
    }

    private void InvalidateWaveforms(SampleManagementEntry? preserve = null)
    {
        foreach (var entry in waveformTokens.Keys.ToArray())
        {
            if (ReferenceEquals(entry, preserve) && entry.Waveform is not null) continue;
            waveformTokens.Remove(entry, out var cancellation); cancellation?.Cancel();
            waveformLoads.Remove(entry); entry.Waveform = null;
            // Dispose after owned reads settle; readers may still register cancellation callbacks.
            if (cancellation is not null) retiredWaveformTokens.Add(cancellation);
        }
    }
    private readonly List<CancellationTokenSource> retiredWaveformTokens = [];
    private async Task StopWaveformsAsync()
    {
        InvalidateWaveforms();
        await Task.WhenAll(waveformOperations);
        foreach (var cancellation in retiredWaveformTokens) cancellation.Dispose();
        retiredWaveformTokens.Clear(); waveformOperations.Clear(); waveformGate.Dispose();
    }
}
