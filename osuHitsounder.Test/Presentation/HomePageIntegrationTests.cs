using CommunityToolkit.Mvvm.Messaging;
using osuHitsounder.Services.FileSystem;
using System.Diagnostics;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using osuHitsounder.Resources.DataModels;
using osuHitsounder.Resources.Messages;
using osuHitsounder.ViewModels.Shell.HomePage;
using osuHitsounder.Views.Pages;

namespace osuHitsounder.Test.Presentation;

[Apartment(ApartmentState.STA)]
[NonParallelizable]
public class HomePageIntegrationTests
{
    private SynchronizationContext? previousContext;
    [SetUp]
    public void SetUp()
    {
        previousContext = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
    }
    [TearDown]
    public void TearDown() => SynchronizationContext.SetSynchronizationContext(previousContext);

    [Test]
    public void NavigatingWhileProjectsLoadCompletesStartupWithoutFailure()
    {
        EnsureResources();
        var messenger = new WeakReferenceMessenger();
        var pending = new TaskCompletionSource<FolderSnapshot>();
        var songsReady = new TaskCompletionSource();
        var folders = new StubHomeFolderService
        {
            Projects = token => pending.Task.WaitAsync(token),
            Songs = (_, _) => { songsReady.SetResult(); return Task.FromResult<FolderSnapshot?>(new FolderSnapshot([])); }
        };
        var vm = CreateVM(folders);
        var window = new MainWindow(new HomePage(vm), messenger, () => new WorkstationPage(), vm);
        window.Show();
        try
        {
            var startup = window.LoadAsync();
            UiTestPump.Run(songsReady.Task);
            messenger.Send(new ProjectCreatedMessage("saved.json"));
            UiTestPump.Run(startup);
            Assert.That(startup.IsCompletedSuccessfully, Is.True);
            Assert.That(((ContentControl)window.FindName("PageHost")).Content, Is.TypeOf<WorkstationPage>());
        }
        finally { window.Close(); }
    }

    [Test]
    public void ClosingShellCancelsOwnedLoad()
    {
        EnsureResources();
        var pending = new TaskCompletionSource<HomeFolderData>();
        var vm = CreateVM(new StubHomeFolderService(token => pending.Task.WaitAsync(token)));
        var window = new MainWindow(new HomePage(vm), new WeakReferenceMessenger(), () => new WorkstationPage(), vm);
        window.Show();
        var load = window.LoadAsync();
        window.Close();
        var frame = new DispatcherFrame();
        load.ContinueWith(_ => window.Dispatcher.BeginInvoke(() => frame.Continue = false));
        Dispatcher.PushFrame(frame);
        Assert.That(load.IsCanceled, Is.True);
        Assert.That(vm.IsLoading, Is.False);
    }

    [Test]
    public void ContainerBuildsShellWithInjectedPageAndViewModel()
    {
        EnsureResources();
        using var provider = App.CreateServices();
        var window = provider.GetRequiredService<MainWindow>();
        var page = provider.GetRequiredService<HomePage>();
        Assert.That(((ContentControl)window.FindName("PageHost")).Content, Is.SameAs(page));
        Assert.That(page.DataContext, Is.SameAs(provider.GetRequiredService<HomePageVM>()));
        window.Close();
    }

    private static void EnsureResources()
    {
        var app = Application.Current ?? new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        app.Resources.MergedDictionaries.Clear();
        UiTestPump.EnsureFontResources(app);
        foreach (var resource in new[] { "Themes/Dark.xaml", "Styles/TextBlock.xaml", "Styles/ItemsControl.xaml", "Styles/Border.xaml", "Styles/ContentPresenter.xaml", "Styles/Button.xaml", "Styles/ToolTip.xaml", "Styles/Window.xaml" })
            app.Resources.MergedDictionaries.Add(new ResourceDictionary
            {
                Source = new Uri($"/osuHitsounder;component/Resources/{resource}", UriKind.Relative)
            });
    }

    [Test]
    public void WorkstationIsCreatedOnlyAfterSuccessAndClosedShellIgnoresMessages()
    {
        EnsureResources();
        var messenger = new WeakReferenceMessenger();
        var vm = CreateVM(new StubHomeFolderService(_ => Task.FromResult(new HomeFolderData([], [], "C:\\osu", true, false))));
        var page = new HomePage(vm);
        var creations = 0;
        var window = new MainWindow(page, messenger, () => { creations++; return new WorkstationPage(); }, vm);
        window.Show();
        try
        {
            UiTestPump.Run(window.LoadAsync());
            var host = (ContentControl)window.FindName("PageHost");
            Assert.That(host.Content, Is.SameAs(page));
            Assert.That(creations, Is.Zero);
            messenger.Send(new ProjectCreatedMessage("saved.json"));
            Assert.That(host.Content, Is.TypeOf<WorkstationPage>());
            Assert.That(host.IsEnabled, Is.True);
            Assert.That(creations, Is.EqualTo(1));
        }
        finally { window.Close(); }
        Assert.That(messenger.IsRegistered<ProjectCreatedMessage>(window), Is.False);
        Assert.That(messenger.IsRegistered<ProjectCreationFailedMessage>(window), Is.False);
        messenger.Send(new ProjectCreatedMessage("late.json"));
        Assert.That(creations, Is.EqualTo(1));
    }

    [Test]
    public void ClosingShellCancelsProjectCreation()
    {
        EnsureResources();
        var pending = new TaskCompletionSource<string>();
        var messenger = new WeakReferenceMessenger();
        var vm = new HomePageVM(new StubHomeFolderService(_ => Task.FromResult(new HomeFolderData([], [], "C:\\osu", true, false))),
            new StubAppSettingsService(), new StubInstallationDialogService(),
            new StubProjectCreationDialogService { Result = "Selected" }, new ImmediateUiScheduler(),
            new StubProjectFileService((_, _, token) => pending.Task.WaitAsync(token)), messenger);
        var window = new MainWindow(new HomePage(vm), messenger, () => throw new AssertionException("Must not navigate"), vm);
        window.Show();
        var operation = vm.CreateProjectCommand.ExecuteAsync(
            new FolderEntry("C:\\Songs\\Selected", "Selected", DateTimeOffset.MinValue));
        window.Close();
        Assert.That(vm.CreateProjectCommand.IsCancellationRequested, Is.True);
        if (!operation.IsCompleted)
        {
            var frame = new DispatcherFrame();
            operation.ContinueWith(_ => window.Dispatcher.BeginInvoke(() => frame.Continue = false));
            Dispatcher.PushFrame(frame);
        }
        operation.GetAwaiter().GetResult();
        Assert.That(vm.IsCreatingProject, Is.False);
    }

    [Test]
    public void SongButtonInvokesSelectedFolderAndDisablesWhileSaving()
    {
        EnsureResources();
        var song = new DirectoryInfo("C:\\Songs\\Selected");
        var pending = new TaskCompletionSource<string>();
        var messenger = new WeakReferenceMessenger();
        DirectoryInfo? selected = null;
        var row = new FolderEntry(song.FullName, song.Name, DateTimeOffset.MinValue)
            { DisplayName = "??? - ?:?" };
        var vm = new HomePageVM(new StubHomeFolderService
            { Songs = (_, _) => Task.FromResult<FolderSnapshot?>(new FolderSnapshot([row])) },
            new StubAppSettingsService(), new StubInstallationDialogService(),
            new StubProjectCreationDialogService { Result = "Selected" }, new ImmediateUiScheduler(),
            new StubProjectFileService((folder, _, _) => { selected = folder; return pending.Task; }), messenger);
        var page = new HomePage(vm);
        var window = new MainWindow(page, messenger, () => new WorkstationPage(), vm);
        window.Show();
        try
        {
            UiTestPump.Run(window.LoadAsync());
            window.UpdateLayout();
            var button = Descendants<Button>(page).Single(button => button.IsVisible);
            var text = Descendants<TextBlock>(button).Single();
            Assert.That(text.Text, Is.EqualTo("??? - ?:?"));
            button.Focus();
            window.UpdateLayout();
            Assert.That(text.Text, Is.EqualTo("??? - ?:?"));
            var tooltip = (ToolTip)button.ToolTip;
            tooltip.PlacementTarget = button;
            tooltip.IsOpen = true;
            window.Dispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
            Assert.That(tooltip.Content, Is.EqualTo(song.FullName));
            tooltip.IsOpen = false;
            Assert.That(((DirectoryInfo)button.CommandParameter).FullName, Is.EqualTo(song.FullName));
            Assert.That(vm.FatalError, Is.Null);
            Assert.That(button.IsEnabled, Is.True);
            var peer = new ButtonAutomationPeer(button);
            Assert.That(peer.GetName(), Is.EqualTo("??? - ?:?"));
            ((IInvokeProvider)peer.GetPattern(PatternInterface.Invoke)).Invoke();
            window.Dispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
            Assert.That(selected!.FullName, Is.EqualTo(song.FullName));
            Assert.That(button.IsEnabled, Is.False);
            Assert.That(((ContentControl)window.FindName("PageHost")).Content, Is.SameAs(page));
            pending.SetResult("saved.json");
            var operation = vm.CreateProjectCommand.ExecutionTask!;
            if (!operation.IsCompleted)
            {
                var frame = new DispatcherFrame();
                operation.ContinueWith(_ => window.Dispatcher.BeginInvoke(() => frame.Continue = false));
                Dispatcher.PushFrame(frame);
            }
            operation.GetAwaiter().GetResult();
            Assert.That(((ContentControl)window.FindName("PageHost")).Content, Is.TypeOf<WorkstationPage>());
        }
        finally { window.Close(); }
    }

    [Test]
    public void PanelsBindAndVirtualizeAcrossSizesAndThemes()
    {
        var app = Application.Current ?? new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        var trace = new StringBuilder();
        using var writer = new StringWriter(trace);
        using var listener = new TextWriterTraceListener(writer);
        PresentationTraceSources.DataBindingSource.Listeners.Add(listener);
        PresentationTraceSources.DataBindingSource.Switch.Level = SourceLevels.Error;
        try
        {
            foreach (var theme in new[] { "Dark", "Light" })
            {
                app.Resources.MergedDictionaries.Clear();
                UiTestPump.EnsureFontResources(app);
                foreach (var resource in new[] { $"Themes/{theme}.xaml", "Styles/TextBlock.xaml", "Styles/ItemsControl.xaml", "Styles/Border.xaml", "Styles/ContentPresenter.xaml", "Styles/Button.xaml", "Styles/ToolTip.xaml", "Styles/Window.xaml" })
                    app.Resources.MergedDictionaries.Add(new ResourceDictionary
                    {
                        Source = new Uri($"/osuHitsounder;component/Resources/{resource}", UriKind.Relative)
                    });

                var folders = Enumerable.Range(0, 2000).Select(i => new DirectoryInfo($"C:\\Songs\\{i:D4} long folder name")).ToArray();
                var vm = CreateVM(new StubHomeFolderService
                {
                    Projects = _ => Task.FromResult(new FolderSnapshot(
                        [new FolderEntry(@"C:\Projects\One", "One", DateTimeOffset.MinValue)])),
                    Songs = (_, _) => Task.FromResult<FolderSnapshot?>(new FolderSnapshot(folders.Select(folder =>
                        new FolderEntry(folder.FullName, folder.Name, DateTimeOffset.MinValue)
                        { DisplayName = "??? - ??????? " + new string('?', 80) })))
                });
                var page = new HomePage(vm);
                var firstRowTimer = Stopwatch.StartNew();
                UiTestPump.Run(page.LoadAsync(CancellationToken.None));
                var window = new Window { Content = page, ShowInTaskbar = false, WindowStyle = WindowStyle.None };
                try
                {
                    window.Show();
                    window.UpdateLayout();
                    Assert.That(Descendants<Button>(page).Any(button => button.IsVisible && button.IsEnabled), Is.True);
                    TestContext.Progress.WriteLine($"Theme={theme} StubFolders=2000 HomePageFirstActionableRowMs={firstRowTimer.Elapsed.TotalMilliseconds:F2}");
                    foreach (var direction in new[] { FlowDirection.LeftToRight, FlowDirection.RightToLeft })
                    foreach (var width in new[] { 640d, 800d, 1280d })
                    {
                        window.FlowDirection = direction;
                        window.Width = width;
                        window.Height = 360;
                        window.UpdateLayout();
                        window.Dispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
                        var lists = Descendants<ItemsControl>(page).ToArray();
                        Assert.That(lists, Has.Length.EqualTo(2));
                        Assert.That(lists[0].Items.Count, Is.EqualTo(1));
                        Assert.That(lists[1].Items.Count, Is.EqualTo(2000));
                        var columns = ((Grid)page.Content).ColumnDefinitions;
                        Assert.That(columns[1].ActualWidth / columns[0].ActualWidth, Is.EqualTo(3).Within(0.01));
                        var panel = Descendants<VirtualizingStackPanel>(lists[1]).Single();
                        Assert.That(panel.Children.Count, Is.InRange(1, 100));
                        var button = Descendants<Button>(lists[1]).First();
                        Assert.That(button.ActualHeight, Is.GreaterThanOrEqualTo(40));
                        Assert.That(button.FlowDirection, Is.EqualTo(direction));
                        button.Focus();
                        window.UpdateLayout();
                        var text = Descendants<TextBlock>(button).Single();
                        Assert.That(text.Text, Is.EqualTo(((FolderEntry)button.DataContext).DisplayName));
                        Assert.That(text.TextWrapping, Is.EqualTo(TextWrapping.Wrap));
                        var scroll = Descendants<ScrollViewer>(lists[1]).Single();
                        var previousCount = lists[1].Items.Count;
                        scroll.ScrollToEnd();
                        window.UpdateLayout();
                        window.Dispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
                        UiTestPump.Run(vm.LoadMoreSongsCommand.ExecutionTask ?? Task.CompletedTask);
                        Assert.That(lists[1].Items.Count, Is.EqualTo(previousCount));
                        Assert.That(scroll.VerticalOffset, Is.GreaterThan(0));
                        Assert.That(panel.Children.Count, Is.LessThan(100));
                    }
                }
                finally { window.Close(); }
            }
            listener.Flush();
            Assert.That(trace.ToString(), Is.Empty);
        }
        finally { PresentationTraceSources.DataBindingSource.Listeners.Remove(listener); }
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
    private static HomePageVM CreateVM(IHomeFolderService folders) =>
        new(folders, new StubAppSettingsService(), new StubInstallationDialogService(),
            new StubProjectCreationDialogService(), new ImmediateUiScheduler(),
            new StubProjectFileService((_, _, _) => Task.FromResult("saved.json")), new WeakReferenceMessenger());
}
