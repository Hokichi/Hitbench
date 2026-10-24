using System.Windows;
using System.Windows.Threading;

namespace Onset.Test.Presentation;

public static class UiTestPump
{
    public static void Layout(FrameworkElement element, double width, double height)
    {
        element.ApplyTemplate();
        element.Measure(new Size(width, height));
        element.Arrange(new Rect(0, 0, width, height));
        element.UpdateLayout();
    }

    public static void EnsureFontResources(Application app)
    {
        foreach (var key in new[] { "Display", "Regular", "Italic", "Bold", "BoldItalic", "Light", "LightItalic" })
            app.Resources[key] = SystemFonts.MessageFontFamily;
    }

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
