using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.Messaging;
using osuHitsounder.Resources.DataModels;
using osuHitsounder.Resources.CustomControls;
using osuHitsounder.Services.Ui;
using osuHitsounder.ViewModels.Shell.HomePage;
using osuHitsounder.Views.Pages;

namespace osuHitsounder.Test.Presentation;

[Apartment(ApartmentState.STA)]
[NonParallelizable]
public class SongBackgroundTests
{
    private SynchronizationContext? previousContext;
    private string path = null!;
    private HomePage page = null!;
    private Window window = null!;
    private InMemoryView view = null!;
    private Image first = null!;
    private Image second = null!;
    private HomePageVM viewModel = null!;

    [SetUp]
    public void SetUp()
    {
        previousContext = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
        var app = Application.Current ?? new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        app.Resources.MergedDictionaries.Clear();
        UiTestPump.EnsureFontResources(app);
        foreach (var resource in new[] { "Themes/Dark.xaml", "Converters/Converters.xaml", "Icons.xaml",
                     "Styles/TextBlock.xaml", "Styles/ItemsControl.xaml", "Styles/Image.xaml", "Styles/Grid.xaml", "Styles/Border.xaml",
                     "Styles/ContentPresenter.xaml", "Styles/Button.xaml", "Styles/ToolTip.xaml", "Styles/TextBox.xaml" })
            app.Resources.MergedDictionaries.Add(new ResourceDictionary
            { Source = new Uri($"/osuHitsounder;component/Resources/{resource}", UriKind.Relative) });
        path = Path.GetTempFileName();
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(BitmapSource.Create(1000, 100, 96, 96,
            PixelFormats.Bgra32, null, new byte[400000], 4000)));
        using (var stream = File.Create(path)) encoder.Save(stream);
        var vm = new HomePageVM(new StubHomeFolderService(), new StubAppSettingsService(),
            new StubInstallationDialogService(), new StubProjectCreationDialogService(), new ImmediateUiScheduler(),
            new StubProjectFileService((_, _, _) => Task.FromResult("saved.json")), new WeakReferenceMessenger());
        viewModel = vm;
        vm.Songs.Add(new FolderEntry(@"C:\Songs\One", "One", DateTimeOffset.MinValue) { BackgroundPath = path });
        vm.Songs.Add(new FolderEntry(@"C:\Songs\Two", "Two", DateTimeOffset.MinValue) { BackgroundPath = path });
        page = new HomePage(vm);
        window = new Window { Content = page, Width = 1920, Height = 1080 };
        view = new InMemoryView(window);
        page.UpdateSongsLayout(new Size(1920, 1080));
        view.UpdateLayout();
        var images = Descendants<Image>(page).ToArray();
        first = images[0];
        second = images[1];
        PumpUntil(() => first.Source is not null && second.Source is not null);
    }

    [TearDown]
    public void TearDown()
    {
        view?.Dispose();
        window?.Close();
        if (path is not null) File.Delete(path);
        SynchronizationContext.SetSynchronizationContext(previousContext);
    }

    [Test]
    public void DuplicateLoadedNotificationKeepsDecodedSource()
    {
        var source = first.Source;
        File.Delete(path);
        page.ImageLoader.Clear();
        first.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
        PumpFor(TimeSpan.FromMilliseconds(100));
        Assert.That(first.Source, Is.SameAs(source));
    }

    [TestCase("Dark", FlowDirection.LeftToRight)]
    [TestCase("Dark", FlowDirection.RightToLeft)]
    [TestCase("Light", FlowDirection.LeftToRight)]
    [TestCase("Light", FlowDirection.RightToLeft)]
    public void ResizingBackgroundFillsCardWithoutReloading(string theme, FlowDirection direction)
    {
        Application.Current.Resources.MergedDictionaries[0].Source =
            new Uri($"/osuHitsounder;component/Resources/Themes/{theme}.xaml", UriKind.Relative);
        window.FlowDirection = direction;
        var source = first.Source;
        foreach (var width in new[] { 1600d, page.MinWidth, 1920d })
        {
            window.Width = width;
            view.UpdateLayout();
            PumpFor(TimeSpan.FromMilliseconds(30));
            var button = Descendants<Button>(page).First(item => ReferenceEquals(item.DataContext, first.DataContext));
            Assert.Multiple(() =>
            {
                Assert.That(first.Source, Is.SameAs(source));
                Assert.That(first.Stretch, Is.EqualTo(Stretch.Fill));
                Assert.That(first.ActualWidth, Is.EqualTo(button.ActualWidth - 2).Within(.01));
                Assert.That(first.ActualHeight, Is.EqualTo(button.ActualHeight - 2).Within(.01));
                var card = (Grid)VisualTreeHelper.GetParent(first);
                Assert.That(((RectangleGeometry)card.Clip).Rect.Size, Is.EqualTo(card.RenderSize));
                Assert.That(((RectangleGeometry)card.Clip).RadiusX, Is.EqualTo(7));
            });
        }
    }

    [Test]
    public void BlurStateOwnsPerImageEffectAndReachesRequestedRadius()
    {
        var button = Descendants<Button>(page).First(item => ReferenceEquals(item.DataContext, first.DataContext));
        Assert.That(second.Effect, Is.Not.SameAs(first.Effect));
        Assert.That(((BlurEffect)first.Effect).Radius, Is.EqualTo(10));
        button.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, 0) { RoutedEvent = UIElement.MouseEnterEvent });
        PumpFor(TimeSpan.FromMilliseconds(220));
        Assert.That(((BlurEffect)first.Effect).Radius, Is.EqualTo(0).Within(.01));
        Assert.That(((BlurEffect)second.Effect).Radius, Is.EqualTo(10));
        button.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, 0) { RoutedEvent = UIElement.MouseLeaveEvent });
        PumpFor(TimeSpan.FromMilliseconds(220));
        Assert.That(((BlurEffect)first.Effect).Radius, Is.EqualTo(10).Within(.01));
    }

    [Test]
    public void RecycledImageRejectsOldSourceAndUnloadsCleanly()
    {
        var row = (FolderEntry)first.DataContext;
        var size = first.RenderSize;
        page.ImageLoader.Clear();
        first.DataContext = row with { BackgroundPath = path + ".missing" };
        first.DataContext = row;
        first.DataContext = row with { BackgroundPath = path + ".missing-again" };
        PumpFor(TimeSpan.FromMilliseconds(100));
        Assert.That(first.Source, Is.Null, "Previous item's completion must not replace a recycled image.");
        first.DataContext = row;
        PumpUntil(() => first.Source is not null);
        Assert.That(first.RenderSize, Is.EqualTo(size));
        page.ImageLoader.Clear();
        first.DataContext = row with { BackgroundPath = path + ".missing" };
        first.DataContext = row;
        first.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent));
        PumpFor(TimeSpan.FromMilliseconds(100));
        Assert.That(first.Source, Is.Null);
    }

    [Test]
    public void ScrollingRecyclesArtworkAndFillsCardsWithPortraitSource()
    {
        var portraitPath = Path.GetTempFileName();
        try
        {
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(BitmapSource.Create(100, 1000, 96, 96,
                PixelFormats.Bgra32, null, new byte[400000], 400)));
            using (var stream = File.Create(portraitPath)) encoder.Save(stream);
            var originalSource = first.Source;
            for (var i = 0; i < 200; i++)
                viewModel.Songs.Add(new FolderEntry($@"C:\Songs\Portrait{i}", $"Portrait{i}", DateTimeOffset.MinValue)
                { BackgroundPath = portraitPath });
            view.UpdateLayout();
            var panel = Descendants<SpacedUniformGrid>(page).Single();
            panel.SetVerticalOffset(double.MaxValue);
            view.UpdateLayout();
            var images = Descendants<Image>(panel).ToArray();
            PumpUntil(() => images.All(image => image.Source is not null));
            view.UpdateLayout();
            Assert.That(images.Length, Is.LessThan(60), "Artwork must remain virtualized.");
            foreach (var image in images)
            {
                var frame = (Grid)VisualTreeHelper.GetParent(image);
                var viewport = frame;
                Assert.Multiple(() =>
                {
                    Assert.That(((FolderEntry)image.DataContext).BackgroundPath, Is.EqualTo(portraitPath));
                    Assert.That(image.Source, Is.Not.SameAs(originalSource));
                    Assert.That(image.ActualWidth, Is.EqualTo(viewport.ActualWidth).Within(.01));
                    Assert.That(image.ActualHeight, Is.EqualTo(viewport.ActualHeight).Within(.01));
                    var center = image.TranslatePoint(new Point(image.ActualWidth / 2, image.ActualHeight / 2), viewport);
                    Assert.That(center.X, Is.EqualTo(viewport.ActualWidth / 2).Within(.01));
                    Assert.That(center.Y, Is.EqualTo(viewport.ActualHeight / 2).Within(.01));
                });
            }
            panel.SetVerticalOffset(0);
            view.UpdateLayout();
            var returned = Descendants<Image>(panel).First();
            PumpUntil(() => ReferenceEquals(returned.Source, originalSource));
            view.UpdateLayout();
            var returnedCard = (Grid)VisualTreeHelper.GetParent(returned);
            Assert.That(returned.ActualWidth, Is.EqualTo(returnedCard.ActualWidth).Within(.01));
            Assert.That(returned.ActualHeight, Is.EqualTo(returnedCard.ActualHeight).Within(.01));
        }
        finally { File.Delete(portraitPath); }
    }

    [Test]
    public void CorruptBackgroundClearsSourceAndCanRecover()
    {
        var corruptPath = Path.GetTempFileName();
        try
        {
            File.WriteAllText(corruptPath, "Not an image");
            var row = (FolderEntry)first.DataContext;
            first.DataContext = row with { BackgroundPath = corruptPath };
            PumpFor(TimeSpan.FromMilliseconds(100));
            Assert.That(first.Source, Is.Null);
            first.DataContext = row;
            PumpUntil(() => first.Source is not null);
        }
        finally { File.Delete(corruptPath); }
    }

    private static IEnumerable<T> Descendants<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T match) yield return match;
            foreach (var descendant in Descendants<T>(child)) yield return descendant;
        }
    }

    private static void PumpUntil(Func<bool> condition)
    {
        var timer = System.Diagnostics.Stopwatch.StartNew();
        while (!condition() && timer.Elapsed < TimeSpan.FromSeconds(5)) PumpFor(TimeSpan.FromMilliseconds(10));
        Assert.That(condition(), Is.True, "Image operation did not complete.");
    }

    private static void PumpFor(TimeSpan duration) => UiTestPump.Run(Task.Delay(duration));

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

}
