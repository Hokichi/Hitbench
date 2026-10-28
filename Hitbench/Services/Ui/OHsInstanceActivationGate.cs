using System.Diagnostics;

namespace Hitbench.Services.Ui;

public sealed class OHsInstanceActivationGate(IUiScheduler scheduler, Action activate)
{
    private readonly object sync = new();
    private bool ready, pending, scheduled, closed;
    public Task Completion { get; private set; } = Task.CompletedTask;
    public void Request() { lock (sync) { if (closed) return; pending = true; Schedule(); } }
    public void MarkReady() { lock (sync) { ready = true; Schedule(); } }
    public void Deactivate() { lock (sync) { closed = true; pending = false; } }
    private void Schedule()
    {
        if (!ready || !pending || scheduled || closed) return;
        scheduled = true;
        Completion = DrainAsync();
    }
    private async Task DrainAsync()
    {
        await Task.Yield();
        try
        {
            await scheduler.InvokeAsync(() =>
            {
                lock (sync)
                {
                    if (closed) return;
                    pending = false;
                }
                activate();
            }, default);
        }
        catch (Exception error) { Trace.TraceError("Instance activation: {0}", error); }
        finally { lock (sync) { scheduled = false; Schedule(); } }
    }
}
