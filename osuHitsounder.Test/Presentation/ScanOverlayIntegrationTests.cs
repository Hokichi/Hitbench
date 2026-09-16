using System.ComponentModel;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
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
            "Styles/ItemsControl.xaml", "Styles/Border.xaml", "Styles/ContentPresenter.xaml", "Styles/Button.xaml",
            "Styles/ToolTip.xaml", "Styles/Window.xaml" })
            app.Resources.MergedDictionaries.Add(new ResourceDictionary
            {
                Source = new Uri($"/osuHitsounder;component/Resources/{resource}", UriKind.Relative)
            });
    }
    [TearDown] public void TearDown() => SynchronizationContext.SetSynchronizationContext(previous);

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
        var window = TestShell.Create(new HomePage(vm), new WeakReferenceMessenger(), () => new WorkstationPage(), vm,
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
        var window = TestShell.Create(new HomePage(vm), new WeakReferenceMessenger(), () => new WorkstationPage(), vm,
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
