using CommunityToolkit.Mvvm.Messaging;
using Hitbench.Resources.Messages;

namespace Hitbench.Services.Ui;

public sealed class OHsAudioOperationProgress(IMessenger messenger, Func<IUiScheduler> getScheduler,
    Func<TimeSpan, CancellationToken, Task>? delay = null)
{
    private readonly AsyncLocal<bool> nested = new();
    public OHsAudioOperationProgress(IMessenger messenger, IUiScheduler scheduler, Func<TimeSpan, CancellationToken, Task>? delay = null)
        : this(messenger, () => scheduler, delay) { }

    public Task RunAsync(string phase, Func<CancellationToken, Task> operation, CancellationToken token) =>
        RunAsync(phase, async t => { await operation(t).ConfigureAwait(false); return true; }, token);

    public async Task<T> RunAsync<T>(string phase, Func<CancellationToken, Task<T>> operation, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (nested.Value) return await operation(token).ConfigureAwait(false);
        nested.Value = true;
        using var timerCancellation = CancellationTokenSource.CreateLinkedTokenSource(token);
        var id = Guid.NewGuid();
        var visible = false;
        var work = operation(token);
        try
        {
            if (!work.IsCompleted)
            {
                var timer = (delay ?? Task.Delay)(TimeSpan.FromMilliseconds(150), timerCancellation.Token);
                await Task.WhenAny(work, timer).ConfigureAwait(false);
                if (timer.IsCompletedSuccessfully && !work.IsCompleted && !token.IsCancellationRequested)
                    await getScheduler().InvokeAsync(() =>
                    {
                        if (work.IsCompleted || token.IsCancellationRequested) return;
                        visible = true;
                        messenger.Send(new OHsAudioBusyChangedMessage(id, true, phase));
                    }, CancellationToken.None).ConfigureAwait(false);
            }
            return await work.ConfigureAwait(false);
        }
        finally
        {
            nested.Value = false;
            timerCancellation.Cancel();
            // Keep ownership even if a presentation recipient fails during the start notification.
            try { await work.ConfigureAwait(false); } catch { /* Cleanup must not replace the original failure. */ }
            if (visible)
                await getScheduler().InvokeAsync(() => messenger.Send(new OHsAudioBusyChangedMessage(id, false, phase)),
                    CancellationToken.None).ConfigureAwait(false);
        }
    }
}
