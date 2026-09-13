namespace osuHitsounder.Services.Ui;

public interface IUiScheduler
{
    Task YieldForRenderAsync(CancellationToken cancellationToken);
}

