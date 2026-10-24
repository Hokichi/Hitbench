namespace Hitbench.Services.Ui;

public interface IUiScheduler
{
    Task YieldForRenderAsync(CancellationToken cancellationToken);
    Task InvokeAsync(Action action, CancellationToken cancellationToken);
}
