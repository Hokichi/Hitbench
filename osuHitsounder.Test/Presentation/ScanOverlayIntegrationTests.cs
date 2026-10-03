using System.ComponentModel;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.Messaging;
using osuHitsounder.Core.Entities;
using osuHitsounder.Resources.DataModels;
using osuHitsounder.Services.Ui;
using osuHitsounder.ViewModels.Shell.HomePage;
using osuHitsounder.Views.Pages;

namespace osuHitsounder.Test.Presentation;

[Apartment(ApartmentState.STA)]
[NonParallelizable]
public class ScanOverlayIntegrationTests
{
    private SynchronizationContext? previous;
    [SetUp] public void SetUp()
    {
        previous = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
        var app = Application.Current ?? new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        app.Resources.MergedDictionaries.Clear();
        UiTestPump.EnsureFontResources(app);
        foreach (var resource in new[] { "Themes/Dark.xaml", "Converters/Converters.xaml", "Icons.xaml", "Styles/TextBlock.xaml",
            "Styles/ItemsControl.xaml", "Styles/Image.xaml", "Styles/Grid.xaml", "Styles/Border.xaml", "Styles/ContentPresenter.xaml", "Styles/Button.xaml",
            "Styles/ToolTip.xaml", "Styles/Window.xaml" })
            app.Resources.MergedDictionaries.Add(new ResourceDictionary
            {
                Source = new Uri($"/osuHitsounder;component/Resources/{resource}", UriKind.Relative)
            });
    }
    [TearDown] public void TearDown() => SynchronizationContext.SetSynchronizationContext(previous);

    [TestCase("Dark")]
    [TestCase("Light")]
    public void SongArtworkFallbackIsBlackAndReadable(string theme)
    {
        Application.Current.Resources.MergedDictionaries[0].Source =
            new Uri($"/osuHitsounder;component/Resources/Themes/{theme}.xaml", UriKind.Relative);
        var page = CreateSongPage();
        var song = new FolderEntry(@"C:\Songs\song", "song", DateTimeOffset.MinValue)
            { Artist = "Artist", Title = "Title", DisplayName = "Artist - Title" };
        var card = (Button)((DataTemplate)page.Resources["HomePageSongTemplate"]).LoadContent();
        card.DataContext = song;
        using var view = new InMemoryView(new Window
        {
            Width = 400, Height = 240,
            Content = new UserControl { DataContext = page.DataContext, Content = card }
        });
        try
        {
            var backdrop = (Grid)card.Content;
            Assert.That((backdrop.Background as SolidColorBrush)?.Color, Is.EqualTo(Colors.Black));
            foreach (var text in Descendants<TextBlock>(backdrop))
                Assert.That((text.Foreground as SolidColorBrush)?.Color, Is.EqualTo(Colors.White));
            Assert.That(Descendants<Image>(backdrop).Single().Source, Is.Null);
            Assert.That(card.Command, Is.Not.Null);
            Assert.That(card.Command!.CanExecute(card.CommandParameter), Is.True);
        }
        finally { page.Deactivate(); }
    }

    [Test]
    public void SongArtworkRecyclingClearsPreviousImage()
    {
        var folder = Directory.CreateTempSubdirectory("song-artwork-");
        var page = CreateSongPage();
        var card = (Button)((DataTemplate)page.Resources["HomePageSongTemplate"]).LoadContent();
        var song = new FolderEntry(folder.FullName, "song", DateTimeOffset.MinValue) { Title = "Title" };
        var valid = Path.Combine(folder.FullName, "valid.png");
        var corrupt = Path.Combine(folder.FullName, "corrupt.png");
        File.WriteAllText(corrupt, "not an image");
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(BitmapSource.Create(1, 1, 96, 96, PixelFormats.Bgra32,
            null, new byte[] { 255, 0, 0, 255 }, 4)));
        using (var stream = File.Create(valid)) encoder.Save(stream);
        card.DataContext = song with { BackgroundPath = valid };
        using var view = new InMemoryView(new Window
        {
            Width = 400, Height = 240,
            Content = new UserControl { DataContext = page.DataContext, Content = card }
        });
        try
        {
            var image = Descendants<Image>(card).Single();
            foreach (var unavailable in new[] { "", Path.Combine(folder.FullName, "missing.png"), corrupt })
            {
                card.DataContext = song with { BackgroundPath = valid };
                PumpUntil(() => image.Source is not null);
                card.DataContext = song with { BackgroundPath = unavailable };
                Assert.That(image.Source, Is.Null, "Recycling must immediately clear the old image.");
                UiTestPump.Run(page.ImageLoader.LoadAsync(unavailable, 336, CancellationToken.None));
                view.UpdateLayout();
                Assert.That(image.Source, Is.Null);
                Assert.That((((Grid)card.Content).Background as SolidColorBrush)?.Color, Is.EqualTo(Colors.Black));
            }
        }
        finally { page.Deactivate(); folder.Delete(true); }
    }

    private static HomePage CreateSongPage() => new(new HomePageVM(new StubHomeFolderService(),
        new StubAppSettingsService(), new StubInstallationDialogService(), new StubProjectCreationDialogService(),
        new UiScheduler(Dispatcher.CurrentDispatcher),
        new StubProjectFileService((_, _, _) => Task.FromResult("saved.json")), new WeakReferenceMessenger()));

    [Test]
    public void SongArtworkFallbackRespectsHighContrast()
    {
        var page = CreateSongPage();
        var card = (Button)((DataTemplate)page.Resources["HomePageSongTemplate"]).LoadContent();
        card.DataContext = new FolderEntry(@"C:\Songs\song", "song", DateTimeOffset.MinValue) { Title = "Title" };
        using var view = new InMemoryView(new Window
        {
            Width = 400, Height = 240,
            Content = new UserControl { DataContext = page.DataContext, Content = card }
        });
        try
        {
            var backdrop = (Grid)card.Content;
            Assert.That(backdrop.Style, Is.Not.Null, "Artwork must have its own high-contrast-aware style.");
            card.Style = WithHighContrast(card.Style);
            backdrop.Style = WithHighContrast(backdrop.Style);
            var image = Descendants<Image>(backdrop).Single();
            image.Style = WithHighContrast(image.Style);
            view.UpdateLayout();
            Assert.That(backdrop.Background, Is.Null, "High contrast must reveal the system button background.");
            Assert.That(image.Visibility, Is.EqualTo(Visibility.Hidden));
            Assert.That((card.Background as SolidColorBrush)?.Color, Is.EqualTo(SystemColors.WindowColor));
            Assert.That((card.Foreground as SolidColorBrush)?.Color, Is.EqualTo(SystemColors.WindowTextColor));
            foreach (var text in Descendants<TextBlock>(backdrop))
                Assert.That((text.Foreground as SolidColorBrush)?.Color, Is.EqualTo(SystemColors.WindowTextColor));
        }
        finally { page.Deactivate(); }
    }

    // Exercise production setters without changing OS accessibility settings.
    private static Style WithHighContrast(Style original)
    {
        var copy = new Style(original.TargetType, original.BasedOn is null ? null : WithHighContrast(original.BasedOn));
        foreach (var setter in original.Setters) copy.Setters.Add(setter);
        foreach (var trigger in original.Triggers)
        {
            if (trigger is DataTrigger data && data.Binding is Binding binding &&
                binding.Path?.PathParameters.OfType<System.Reflection.PropertyInfo>().Any(property =>
                    property.DeclaringType == typeof(SystemParameters) && property.Name == nameof(SystemParameters.HighContrast)) == true)
            {
                var replacement = new DataTrigger { Binding = new Binding { Source = true, Mode = BindingMode.OneWay }, Value = data.Value };
                foreach (var setter in data.Setters) replacement.Setters.Add(setter);
                copy.Triggers.Add(replacement);
            }
            else copy.Triggers.Add(trigger);
        }
        return copy;
    }

    [TestCase("Dark")]
    [TestCase("Light")]
    public void ScanBlursOnlyBackgroundAndBlocksContentUntilRecoveryFinishes(string theme)
    {
        Application.Current.Resources.MergedDictionaries[0].Source =
            new Uri($"/osuHitsounder;component/Resources/Themes/{theme}.xaml", UriKind.Relative);
        var pending = new TaskCompletionSource<OsuSearchResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var picker = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var picking = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var songs = new TaskCompletionSource<FolderSnapshot?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var readingSongs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var vm = new HomePageVM(new StubHomeFolderService
            {
                Find = _ => { started.SetResult(); return pending.Task; },
                Songs = (_, token) => { readingSongs.SetResult(); return songs.Task.WaitAsync(token); }
            },
            new StubAppSettingsService { Load = _ => Task.FromResult(new AppSettings()) },
            new StubInstallationDialogService { Locate = _ => { picking.SetResult(); return picker.Task; } },
            new StubProjectCreationDialogService(), new UiScheduler(Dispatcher.CurrentDispatcher),
            new StubProjectFileService((_, _, _) => Task.FromResult("saved.json")),
            new WeakReferenceMessenger());
        var window = TestShell.Create(new HomePage(vm), new WeakReferenceMessenger(), WorkstationTestPage.Create, vm,
            new StubAppSettingsService());
        using var view = new InMemoryView(window);
        try
        {
            var load = window.LoadAsync();
            UiTestPump.Run(started.Task);
            view.UpdateLayout();
            var host = (ContentControl)window.FindName("PageHost");
            Assert.That(host.IsEnabled, Is.False);
            var label = Descendants<TextBlock>(view.Root).Single(text => text.IsVisible && text.Text == "Processing...");
            Assert.That(Descendants<Border>(view.Root).Any(border => border.Effect is BlurEffect), Is.True);
            for (DependencyObject? parent = label; parent is not null; parent = VisualTreeHelper.GetParent(parent))
                if (parent is UIElement element) Assert.That(element.Effect, Is.Null, "Overlay label must remain sharp.");
            pending.SetResult(new OsuSearchResult(null, false));
            UiTestPump.Run(picking.Task);
            view.UpdateLayout();
            Assert.That(label.IsVisible, Is.True);
            Assert.That(host.IsEnabled, Is.False, "Input gate must survive the picker transition.");
            picker.SetResult(@"C:\chosen");
            UiTestPump.Run(readingSongs.Task);
            view.UpdateLayout();
            Assert.That(label.IsVisible, Is.True);
            Assert.That(host.IsEnabled, Is.False);
            songs.SetResult(new FolderSnapshot(Enumerable.Range(0, 130)
                .Select(i => new FolderEntry($@"C:\Songs\{i}", $"{i}", DateTimeOffset.MinValue))));
            UiTestPump.Run(load);
            view.UpdateLayout();
            var songsList = Descendants<ItemsControl>(view.Root)
                .Single(list => AutomationProperties.GetName(list) == "Songs");
            Assert.That(vm.Songs, Has.Count.EqualTo(HomePageVM.InitialSongCount),
                "Nine rows must publish before Processing clears.");
            PumpUntil(() => Math.Abs(songsList.Opacity - 1) < 0.001);
            Assert.That(label.IsVisible, Is.False);
            Assert.That(host.IsEnabled, Is.True);
            Assert.That(vm.RequiresShutdown, Is.False);
            PumpUntil(() => vm.Songs.Count > 0);
            Assert.That(vm.Songs.Count, Is.LessThan(130));
        }
        finally { window.Close(); }
    }

    [TestCase(true)]
    [TestCase(false)]
    public void ProcessingEndsAfterInitialViewportPublishesSongs(bool configured)
    {
        var songs = new TaskCompletionSource<FolderSnapshot?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var reading = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var vm = new HomePageVM(new StubHomeFolderService
            {
                Find = _ => configured ? throw new AssertionException("Saved path must not scan")
                    : Task.FromResult(new OsuSearchResult(@"C:\osu", false)),
                Songs = (_, token) => { reading.SetResult(); return songs.Task.WaitAsync(token); }
            },
            new StubAppSettingsService { Load = _ => Task.FromResult(new AppSettings { OsuPath = configured ? @"C:\osu" : "" }) },
            new StubInstallationDialogService { Locate = _ => throw new AssertionException("Valid installation must not recover") },
            new StubProjectCreationDialogService(), new UiScheduler(Dispatcher.CurrentDispatcher),
            new StubProjectFileService((_, _, _) => Task.FromResult("saved.json")),
            new WeakReferenceMessenger());
        var window = TestShell.Create(new HomePage(vm), new WeakReferenceMessenger(), WorkstationTestPage.Create, vm,
            new StubAppSettingsService());
        using var view = new InMemoryView(window);
        try
        {
            var load = window.LoadAsync();
            UiTestPump.Run(reading.Task);
            view.UpdateLayout();
            var label = (TextBlock)window.FindName("ProcessingText");
            var host = (ContentControl)window.FindName("PageHost");
            Assert.That(label.IsVisible, Is.True);
            Assert.That(host.IsEnabled, Is.False);
            var states = new List<bool>();
            vm.Songs.CollectionChanged += (_, _) => states.Add(label.IsVisible && !host.IsEnabled);
            songs.SetResult(new FolderSnapshot(Enumerable.Range(0, 130)
                .Select(i => new FolderEntry($@"C:\Songs\{i}", $"{i}", DateTimeOffset.MinValue))));
            UiTestPump.Run(load);
            view.UpdateLayout();
            var songsList = Descendants<ItemsControl>(view.Root)
                .Single(list => AutomationProperties.GetName(list) == "Songs");
            Assert.That(vm.Songs, Has.Count.EqualTo(HomePageVM.InitialSongCount));
            PumpUntil(() => Math.Abs(songsList.Opacity - 1) < 0.001);
            Assert.That(states, Is.Not.Empty.And.All.True);
            Assert.That(label.IsVisible, Is.False);
            Assert.That(host.IsEnabled, Is.True);
            Assert.That(vm.Songs.Count, Is.LessThan(130));
        }
        finally { window.Close(); }
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
        if (condition()) return;
        var frame = new DispatcherFrame();
        var poll = new DispatcherTimer(TimeSpan.FromMilliseconds(10), DispatcherPriority.Background,
            (_, _) =>
            {
                if (condition()) frame.Continue = false;
            }, Dispatcher.CurrentDispatcher);
        var timeout = new DispatcherTimer(TimeSpan.FromSeconds(8), DispatcherPriority.Send,
            (_, _) => frame.Continue = false, Dispatcher.CurrentDispatcher);
        Dispatcher.PushFrame(frame);
        poll.Stop();
        timeout.Stop();
        Assert.That(condition(), Is.True, "Expected UI state was not reached.");
    }
}
