using System.Windows.Threading;

namespace osuHitsounder.Services.Ui;

public sealed class UiScheduler(Dispatcher dispatcher) : IUiScheduler
{
    public Task YieldForRenderAsync(CancellationToken cancellationToken) =>
        dispatcher.InvokeAsync(() => { }, DispatcherPriority.Background, cancellationToken).Task;
}

