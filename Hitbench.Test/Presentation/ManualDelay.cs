namespace Hitbench.Test.Presentation;

public sealed class ManualDelay(ManualTimeProvider clock)
{
    private readonly object gate = new();
    private readonly List<(long Due, TaskCompletionSource Completion)> pending = [];
    private int requests;
    public int RequestCount => Volatile.Read(ref requests);
    public int PendingCount { get { lock (gate) return pending.Count; } }
    public async Task WaitAsync(TimeSpan duration, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var item = (clock.GetTimestamp() + duration.Ticks, completion);
        lock (gate) pending.Add(item);
        Interlocked.Increment(ref requests);
        using var registration = token.Register(() => completion.TrySetCanceled(token));
        try { await completion.Task; }
        finally { lock (gate) pending.Remove(item); }
    }
    public void Advance(TimeSpan elapsed)
    {
        clock.Advance(elapsed);
        TaskCompletionSource[] ready;
        lock (gate) ready = pending.Where(item => item.Due <= clock.GetTimestamp()).Select(item => item.Completion).ToArray();
        foreach (var completion in ready) completion.TrySetResult();
    }
}
