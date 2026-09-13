using System.Windows.Threading;

namespace osuHitsounder.Test.Presentation;

public static class UiTestPump
{
    public static void Run(Task task)
    {
        if (!task.IsCompleted)
        {
            var dispatcher = Dispatcher.CurrentDispatcher;
            var frame = new DispatcherFrame();
            var timer = new DispatcherTimer(TimeSpan.FromSeconds(8), DispatcherPriority.Send,
                (_, _) => frame.Continue = false, dispatcher);
            var completion = task.ContinueWith(_ => dispatcher.BeginInvoke(() => frame.Continue = false),
                TaskScheduler.Default);
            Dispatcher.PushFrame(frame);
            timer.Stop();
            Assert.That(task.IsCompleted, Is.True, "UI operation did not finish.");
            GC.KeepAlive(completion);
        }
        task.GetAwaiter().GetResult();
    }
}

