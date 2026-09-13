using osuHitsounder.Services.Ui;

namespace osuHitsounder.Test.Presentation;

public sealed class ImmediateUiScheduler : IUiScheduler
{
    public Func<CancellationToken, Task> Yield { get; set; } = _ => Task.CompletedTask;
    public Task YieldForRenderAsync(CancellationToken token) => Yield(token);
}

