using System.Windows.Threading;

namespace Onset.Services.Ui;

public sealed class UiScheduler(Dispatcher dispatcher) : IUiScheduler
{
    public Task YieldForRenderAsync(CancellationToken cancellationToken) =>
        dispatcher.InvokeAsync(() => { }, DispatcherPriority.Background, cancellationToken).Task;

    public Task InvokeAsync(Action action, CancellationToken cancellationToken) =>
        dispatcher.InvokeAsync(action, DispatcherPriority.DataBind, cancellationToken).Task;
}
