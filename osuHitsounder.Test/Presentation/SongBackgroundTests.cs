using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using osuHitsounder.Resources.CustomControls;
using osuHitsounder.Services.Ui;

namespace osuHitsounder.Test.Presentation;

[Apartment(ApartmentState.STA)]
[NonParallelizable]
public class SongBackgroundTests
{
    [Test]
    public void BlurStateOwnsPerInstanceEffectAndReachesRequestedRadius()
    {
        var previous = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
        var first = new OHsSongBackground();
        var second = new OHsSongBackground();
        var panel = new System.Windows.Controls.StackPanel { Children = { first, second } };
        var window = new Window { Content = panel, Width = 200, Height = 200, ShowInTaskbar = false };
        try
        {
            using var view = new InMemoryView(window);
            Assert.That(first.Effect, Is.TypeOf<System.Windows.Media.Effects.BlurEffect>());
            Assert.That(second.Effect, Is.TypeOf<System.Windows.Media.Effects.BlurEffect>());
            Assert.That(second.Effect, Is.Not.SameAs(first.Effect));
            Assert.That(((System.Windows.Media.Effects.BlurEffect)first.Effect).Radius, Is.EqualTo(10));

            first.IsBlurred = false;
            PumpFor(TimeSpan.FromMilliseconds(150));

            Assert.That(((System.Windows.Media.Effects.BlurEffect)first.Effect).Radius, Is.EqualTo(0).Within(0.01));
            Assert.That(((System.Windows.Media.Effects.BlurEffect)second.Effect).Radius, Is.EqualTo(10));

            first.IsBlurred = true;
            PumpFor(TimeSpan.FromMilliseconds(150));
            Assert.That(((System.Windows.Media.Effects.BlurEffect)first.Effect).Radius, Is.EqualTo(10).Within(0.01));
        }
        finally
        {
            window.Close();
            SynchronizationContext.SetSynchronizationContext(previous);
        }
    }

    [Test]
    [Explicit("Local decode responsiveness measurement; no hardware-dependent latency assertion")]
    public void MeasureImageDecodeResponsiveness()
    {
        var previous = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
        var path = Path.GetTempFileName();
        try
        {
            var pixels = new byte[3840 * 2160 * 4];
            new Random(42).NextBytes(pixels);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(BitmapSource.Create(3840, 2160, 96, 96,
                PixelFormats.Bgra32, null, pixels, 3840 * 4)));
            using (var stream = File.Create(path)) encoder.Save(stream);
            var clock = System.Diagnostics.Stopwatch.StartNew();
            // Reproduce the old converter's synchronous, full-size decode on this UI thread.
            for (var i = 0; i < 16; i++)
            {
                using var stream = File.OpenRead(path);
                var image = new BitmapImage();
                image.BeginInit();
                image.CacheOption = BitmapCacheOption.OnLoad;
                image.StreamSource = stream;
                image.EndInit();
                image.Freeze();
            }
            var synchronousMs = clock.Elapsed.TotalMilliseconds;
            var loader = new SongImageLoader();
            clock.Restart();
            var requests = Enumerable.Range(0, 16).Select(i => loader.LoadAsync(path, 336 + i,
                CancellationToken.None)).ToArray();
            var dispatchMs = clock.Elapsed.TotalMilliseconds;
            var ticks = 0;
            var last = clock.Elapsed.TotalMilliseconds;
            var maximumGap = 0d;
            var timer = new DispatcherTimer(TimeSpan.FromMilliseconds(1), DispatcherPriority.Input,
                (_, _) =>
                {
                    var now = clock.Elapsed.TotalMilliseconds;
                    maximumGap = Math.Max(maximumGap, now - last);
                    last = now;
                    ticks++;
                }, Dispatcher.CurrentDispatcher);
            try { UiTestPump.Run(Task.WhenAll(requests)); }
            finally { timer.Stop(); }
            Assert.That(requests.All(task => task.Result is { IsFrozen: true }), Is.True);
            Assert.That(ticks, Is.GreaterThan(0));
            TestContext.Progress.WriteLine($"Images=16 FullSizeUiBlockMs={synchronousMs:F2} AsyncDispatchMs={dispatchMs:F2} InputTicks={ticks} MaxInputGapMs={maximumGap:F2}");
        }
        finally
        {
            File.Delete(path);
            SynchronizationContext.SetSynchronizationContext(previous);
        }
    }

    [Test]
    public void RecycledBackgroundRejectsOldImageAndUnloadsCleanly()
    {
        var previous = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
        var path = Path.GetTempFileName();
        var background = new OHsSongBackground { Loader = new SongImageLoader(), Path = path };
        var window = new Window { Content = background, Width = 336, Height = 108, ShowInTaskbar = false };
        try
        {
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(BitmapSource.Create(1, 1, 96, 96, PixelFormats.Bgra32,
                null, new byte[] { 0, 0, 255, 255 }, 4)));
            using (var stream = File.Create(path)) encoder.Save(stream);
            using var view = new InMemoryView(window);
            view.UpdateLayout();
            var first = background.LoadingTask;
            background.Path = path + ".missing";
            UiTestPump.Run(Task.WhenAll(first, background.LoadingTask));
            Assert.That(background.ImageBrush, Is.Null, "Recycled item must not receive previous image.");
            background.Path = path;
            UiTestPump.Run(background.LoadingTask);
            Assert.That(background.ImageBrush, Is.Not.Null);
            background.Path = path + ".missing-again";
            view.Dispose();
            UiTestPump.Run(background.LoadingTask);
            Assert.That(background.ImageBrush, Is.Null);
        }
        finally
        {
            window.Close();
            File.Delete(path);
            SynchronizationContext.SetSynchronizationContext(previous);
        }
    }

    private static void PumpFor(TimeSpan duration)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer(duration, DispatcherPriority.Background,
            (_, _) => frame.Continue = false, Dispatcher.CurrentDispatcher);
        Dispatcher.PushFrame(frame);
        timer.Stop();
    }
}
