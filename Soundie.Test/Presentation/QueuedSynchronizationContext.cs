namespace Soundie.Test.Presentation;

internal sealed class QueuedSynchronizationContext : SynchronizationContext
{
    private readonly Queue<(SendOrPostCallback Callback, object? State)> pending = new();
    public override void Post(SendOrPostCallback d, object? state) { lock (pending) pending.Enqueue((d, state)); }
    public void Drain()
    {
        while (true)
        {
            (SendOrPostCallback Callback, object? State) next;
            lock (pending) { if (!pending.TryDequeue(out next)) return; }
            next.Callback(next.State);
        }
    }
}
