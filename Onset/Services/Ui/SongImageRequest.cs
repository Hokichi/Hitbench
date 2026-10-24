namespace Onset.Services.Ui;

internal sealed class SongImageRequest
{
    public CancellationTokenSource? Cancellation { get; set; } = new();
    public Task LoadingTask { get; set; } = Task.CompletedTask;
}
