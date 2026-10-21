using osuHitsounder.Services.Ui;

namespace osuHitsounder.Test.Presentation;

public sealed class ImmediateUiScheduler : IUiScheduler
{
    public Func<Action, CancellationToken, Task>? Invocation { get; set; }
    public Func<CancellationToken, Task> Yield { get; set; } = _ => Task.CompletedTask;
    public Task YieldForRenderAsync(CancellationToken token) => Yield(token);
    public Task InvokeAsync(Action action, CancellationToken token)
    {
        if (Invocation is { } invoke) return invoke(action, token);
        token.ThrowIfCancellationRequested();
        action();
        return Task.CompletedTask;
    }
}
