using System.Windows.Threading;

namespace osuHitsounder.Services.Ui;

public sealed class AudioOutputMonitor : IDisposable
{
    private readonly DispatcherTimer timer;
    public AudioOutputMonitor(Action poll)
    {
        timer = new DispatcherTimer(TimeSpan.FromMilliseconds(10), DispatcherPriority.Background,
            (_, _) => poll(), Dispatcher.CurrentDispatcher);
    }
    public void Dispose() => timer.Stop();
}
