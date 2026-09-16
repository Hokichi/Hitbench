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
using osuHitsounder.Core.Entities;
using osuHitsounder.Resources.DataModels;
using osuHitsounder.Resources.CustomControls;
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
    public void ApplyingWindowSettingsRestoresSavedBoundsAndState()
    {
        EnsureResources();
        var vm = CreateVM(new StubHomeFolderService());
        var window = new MainWindow(new HomePage(vm), new WeakReferenceMessenger(),
            () => new WorkstationPage(), vm, new StubAppSettingsService());
        try
        {
            window.ApplySettings(new AppSettings
            {
                RestoredWidth = 400,
                RestoredHeight = 200,
                IsMaximized = true
            });

            Assert.That(window.Width, Is.EqualTo(window.MinWidth));
            Assert.That(window.Height, Is.EqualTo(window.MinHeight));
            Assert.That(window.WindowState, Is.EqualTo(WindowState.Maximized));
        }
        finally { window.Close(); }
    }

    [Test]
    public void ClosingAfterRestoredResizePersistsLatestPlacement()
    {
        EnsureResources();
        (bool IsMaximized, double Width, double Height)? saved = null;
        var settings = new StubAppSettingsService
        {
            SaveWindowPlacement = (placement, _) =>
            {
                saved = placement;
                return Task.CompletedTask;
            }
        };
        var vm = CreateVM(new StubHomeFolderService());
        var window = new MainWindow(new HomePage(vm), new WeakReferenceMessenger(),
            () => new WorkstationPage(), vm, settings);
        window.ApplySettings(new AppSettings());
        window.Show();
        window.Width = 1024;
        window.Height = 640;

        window.Close();

        Assert.That(saved, Is.EqualTo((false, 1024d, 640d)));
    }

    [Test]
    public void CaptionButtonsToggleStateWithoutTreatingMinimizeAsRestore()
    {
        EnsureResources();
        (bool IsMaximized, double Width, double Height)? saved = null;
        var settings = new StubAppSettingsService
        {
            SaveWindowPlacement = (placement, _) =>
            {
                saved = placement;
                return Task.CompletedTask;
            }
        };
        var vm = CreateVM(new StubHomeFolderService());
        var window = new MainWindow(new HomePage(vm), new WeakReferenceMessenger(),
            () => new WorkstationPage(), vm, settings);
        window.ApplySettings(new AppSettings { RestoredWidth = 1024, RestoredHeight = 640 });
        window.Show();
        try
        {
            var maximize = (Button)window.FindName("MaximizeRestoreButton");
            var minimize = (Button)window.FindName("MinimizeButton");

            ((IInvokeProvider)new ButtonAutomationPeer(maximize).GetPattern(PatternInterface.Invoke)!).Invoke();
            window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
            Assert.That(window.WindowState, Is.EqualTo(WindowState.Maximized));
            ((IInvokeProvider)new ButtonAutomationPeer(minimize).GetPattern(PatternInterface.Invoke)!).Invoke();
            window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
            Assert.That(window.WindowState, Is.EqualTo(WindowState.Minimized));
        }
        finally { window.Close(); }

        Assert.That(saved, Is.EqualTo((true, 1024d, 640d)));
    }

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
        var window = new MainWindow(new HomePage(vm), messenger, () => new WorkstationPage(), vm,
            new StubAppSettingsService());
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
        var window = new MainWindow(new HomePage(vm), new WeakReferenceMessenger(), () => new WorkstationPage(), vm,
            new StubAppSettingsService());
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
    public void CancellationWhileWaitingForVisibleViewportClearsProcessing()
    {
        EnsureResources();
        var vm = CreateVM(new StubHomeFolderService());
        var page = new HomePage(vm) { Visibility = Visibility.Hidden };
        var window = new Window { Content = page, ShowInTaskbar = false };
        using var cancellation = new CancellationTokenSource();
        window.Show();
        try
        {
            var loading = page.LoadAsync(cancellation.Token);
            UiTestPump.Run(vm.LoadAsync(cancellation.Token));
            Assert.That(vm.IsProcessing, Is.True);
            cancellation.Cancel();
            Assert.Catch<OperationCanceledException>(() => UiTestPump.Run(loading));
            Assert.That(vm.IsProcessing, Is.False);
        }
        finally { window.Close(); }
    }

    [Test]
    public void HiddenViewportResumesPresentationWhenShown()
    {
        EnsureResources();
        var vm = CreateVM(new StubHomeFolderService());
        var page = new HomePage(vm) { Visibility = Visibility.Hidden };
        var window = new Window { Content = page, ShowInTaskbar = false };
        window.Show();
        try
        {
            var loading = page.LoadAsync(CancellationToken.None);
            UiTestPump.Run(vm.LoadAsync(CancellationToken.None));
            Assert.That(vm.IsProcessing, Is.True);
            page.Visibility = Visibility.Visible;
            UiTestPump.Run(loading);
            Assert.That(vm.IsProcessing, Is.False);
        }
        finally { window.Close(); }
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
        foreach (var resource in new[] { "Themes/Dark.xaml", "Icons.xaml", "Styles/TextBlock.xaml", "Styles/ItemsControl.xaml", "Styles/Border.xaml", "Styles/ContentPresenter.xaml", "Styles/Button.xaml", "Styles/ToolTip.xaml", "Styles/Window.xaml" })
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
        var window = new MainWindow(page, messenger, () => { creations++; return new WorkstationPage(); }, vm,
            new StubAppSettingsService());
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
        var window = new MainWindow(new HomePage(vm), messenger,
            () => throw new AssertionException("Must not navigate"), vm, new StubAppSettingsService());
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
        var background = CreateTestImage();
        var pending = new TaskCompletionSource<string>();
        var messenger = new WeakReferenceMessenger();
        DirectoryInfo? selected = null;
        var row = new FolderEntry(song.FullName, song.Name, DateTimeOffset.MinValue)
            { Artist = "Artist", Title = "Title", DisplayName = "Artist - Title", BackgroundPath = background };
        var vm = new HomePageVM(new StubHomeFolderService
            { Songs = (_, _) => Task.FromResult<FolderSnapshot?>(new FolderSnapshot([row])) },
            new StubAppSettingsService(), new StubInstallationDialogService(),
            new StubProjectCreationDialogService { Result = "Selected" }, new ImmediateUiScheduler(),
            new StubProjectFileService((folder, _, _) => { selected = folder; return pending.Task; }), messenger);
        var page = new HomePage(vm);
        var window = new MainWindow(page, messenger, () => new WorkstationPage(), vm,
            new StubAppSettingsService());
        window.Show();
        try
        {
            UiTestPump.Run(window.LoadAsync());
            PumpUntil(() => vm.Songs.Count == 1);
            var button = Descendants<Button>(page).Single(button => button.IsVisible && button.DataContext is FolderEntry);
            var text = Descendants<TextBlock>(button).ToArray();
            var backgroundControl = Descendants<OHsSongBackground>(button).Single();
            Assert.That(backgroundControl.LoadingTask.IsCompleted, Is.True);
            Assert.That(backgroundControl.ImageBrush, Is.Not.Null, "Initial background must be ready when loading completes.");
            Assert.That(text.Select(value => value.Text), Is.EqualTo(new[] { "Artist", "Title" }));
            button.Focus();
            window.UpdateLayout();
            Assert.That(button.ToolTip, Is.Null);
            Assert.That(button.IsTabStop, Is.True);
            Assert.That(button.Opacity, Is.EqualTo(1));
            Assert.That(button.Effect, Is.Null);
            PumpFor(TimeSpan.FromMilliseconds(150));
            Assert.That(((System.Windows.Media.Effects.BlurEffect)backgroundControl.Effect).Radius,
                Is.EqualTo(0).Within(0.01));
            Assert.That(button.CommandParameter, Is.SameAs(row));
            Assert.That(vm.FatalError, Is.Null);
            Assert.That(button.IsEnabled, Is.True);
            var peer = new ButtonAutomationPeer(button);
            Assert.That(peer.GetName(), Is.EqualTo("Artist - Title"));
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
        finally
        {
            window.Close();
            Directory.Delete(Path.GetDirectoryName(background)!, true);
        }
    }

    [Test]
    public void SongCardsUseFixedVirtualizedGridAcrossSizesAndThemes()
    {
        var app = Application.Current ?? new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        var background = CreateTestImage();
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
                        {
                            Artist = "Artist",
                            Title = "Title " + new string('x', 80),
                            DisplayName = "Artist - Title",
                            BackgroundPath = background
                        })))
                });
                var page = new HomePage(vm);
                var firstRowTimer = Stopwatch.StartNew();
                var window = new Window
                {
                    Content = page,
                    Width = 1920,
                    Height = 1080,
                    ShowInTaskbar = false,
                    WindowStyle = WindowStyle.None
                };
                try
                {
                    window.Show();
                    UiTestPump.Run(page.LoadAsync(CancellationToken.None));
                    PumpUntil(() => vm.Songs.Count == 36);
                    window.UpdateLayout();
                    TestContext.Progress.WriteLine($"Theme={theme} StubFolders=2000 HomePageFirstActionableRowMs={firstRowTimer.Elapsed.TotalMilliseconds:F2}");
                    var lists = Descendants<ItemsControl>(page).ToArray();
                    var songsList = lists.Single(list => AutomationProperties.GetName(list) == "Songs");
                    var panel = Descendants<SpacedUniformGrid>(songsList).Single();
                    var button = Descendants<Button>(songsList)
                        .First(candidate => candidate.DataContext is FolderEntry);
                    var labels = Descendants<TextBlock>(button).ToArray();
                    var backgroundBorder = Descendants<Border>(button)
                        .First(border => border.Background is ImageBrush);
                    var peer = new ButtonAutomationPeer(button);
                    Assert.Multiple(() =>
                    {
                        Assert.That(lists, Has.Length.EqualTo(2));
                        Assert.That(lists[0].Items.Count, Is.EqualTo(1));
                        Assert.That(songsList.Items.Count, Is.EqualTo(36));
                        var columns = ((Grid)page.Content).ColumnDefinitions;
                        Assert.That(columns[1].ActualWidth / columns[0].ActualWidth, Is.EqualTo(3).Within(0.01));
                        Assert.That(panel.Columns, Is.EqualTo(4));
                        Assert.That(panel.ItemWidth, Is.EqualTo(336));
                        Assert.That(panel.ItemHeight, Is.EqualTo(108));
                        Assert.That(panel.HorizontalGap, Is.EqualTo(12));
                        Assert.That(panel.VerticalGap, Is.EqualTo(12));
                        Assert.That(panel.Children.Count, Is.InRange(1, 36));
                        Assert.That(button.ActualWidth, Is.EqualTo(336).Within(0.01));
                        Assert.That(button.ActualHeight, Is.EqualTo(108).Within(0.01));
                        Assert.That(backgroundBorder.ActualHeight, Is.GreaterThanOrEqualTo(106));
                        Assert.That(labels.Select(label => label.Text), Is.EqualTo(new[]
                            { "Artist", "Title " + new string('x', 80) }));
                        Assert.That(((SolidColorBrush)labels[0].Foreground).Color,
                            Is.EqualTo(((SolidColorBrush)app.FindResource("Brush.Text.Primary")).Color));
                        Assert.That(labels[0].TranslatePoint(new Point(), button).Y,
                            Is.LessThan(labels[1].TranslatePoint(new Point(), button).Y));
                        Assert.That(((ImageBrush)backgroundBorder.Background).Stretch, Is.EqualTo(Stretch.UniformToFill));
                        Assert.That(peer.GetName(), Is.EqualTo("Artist - Title"));
                        Assert.That(peer.GetHelpText(), Does.Contain("long folder name"));
                    });

                    window.FlowDirection = FlowDirection.RightToLeft;
                    window.Width = 640;
                    window.Height = 360;
                    window.UpdateLayout();
                    window.Dispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
                    var scroll = Descendants<ScrollViewer>(songsList).Single();
                    Assert.Multiple(() =>
                    {
                        Assert.That(panel.Columns, Is.EqualTo(4));
                        Assert.That(scroll.ScrollableWidth, Is.GreaterThan(0));
                        Assert.That(scroll.ScrollableHeight, Is.GreaterThan(0));
                        Assert.That(panel.ExtentWidth, Is.EqualTo(1380));
                    });
                }
                finally { window.Close(); }
            }
            listener.Flush();
            Assert.That(trace.ToString(), Is.Empty);
        }
        finally
        {
            PresentationTraceSources.DataBindingSource.Listeners.Remove(listener);
            Directory.Delete(Path.GetDirectoryName(background)!, true);
        }
    }

    [Test]
    public void SongsLoadOnlyAfterInitialLayoutAndGenuineThresholdScrolls()
    {
        EnsureResources();
        var background = CreateTestImage();
        var rows = Enumerable.Range(0, 200).Select(index =>
            new FolderEntry($@"C:\Songs\{index:D3}", $"{index:D3}", DateTimeOffset.UnixEpoch.AddMinutes(index))
            {
                Artist = "Artist",
                Title = $"Title {index}",
                DisplayName = $"Artist - Title {index}",
                BackgroundPath = background
            });
        var vm = CreateVM(new StubHomeFolderService
        {
            Songs = (_, _) => Task.FromResult<FolderSnapshot?>(new FolderSnapshot(rows))
        });
        var page = new HomePage(vm);
        var window = new Window
        {
            Content = page,
            Width = 1920,
            Height = 1080,
            ShowInTaskbar = false,
            WindowStyle = WindowStyle.None
        };
        window.Show();
        try
        {
            var songsList = (ItemsControl)page.FindName("SongsList");
            Assert.That(songsList.Opacity, Is.Zero);
            UiTestPump.Run(page.LoadAsync(CancellationToken.None));
            PumpUntil(() => vm.Songs.Count == HomePageVM.InitialSongCount);
            PumpUntil(() => Math.Abs(songsList.Opacity - 1) < 0.001);
            var panel = Descendants<SpacedUniformGrid>(songsList).Single();
            var scroll = Descendants<ScrollViewer>(songsList).Single();

            window.Width = 1900;
            window.UpdateLayout();
            window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
            Assert.That(vm.Songs, Has.Count.EqualTo(36), "Resize/layout must not count as user scrolling.");

            scroll.ScrollToVerticalOffset(1);
            PumpUntil(() => vm.Songs.Count == 68);

            scroll.ScrollToEnd();
            scroll.ScrollToVerticalOffset(Math.Max(0, scroll.ScrollableHeight - 1));
            scroll.ScrollToEnd();
            PumpUntil(() => vm.Songs.Count == 100);
            window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
            Assert.That(vm.Songs, Has.Count.EqualTo(100), "Repeated notifications must coalesce to one batch.");

            scroll.ScrollToVerticalOffset(Math.Max(0, scroll.ScrollableHeight - panel.RowStride * 2));
            window.UpdateLayout();
            window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
            Assert.That(vm.Songs, Has.Count.EqualTo(100));
            scroll.LineDown();
            PumpUntil(() => vm.Songs.Count == 132);
        }
        finally
        {
            window.Close();
            Directory.Delete(Path.GetDirectoryName(background)!, true);
        }
    }

    [Test]
    public void InitialPresentationWarmsEveryBackgroundAndMissingImagesDoNotBlockReveal()
    {
        EnsureResources();
        var imageDirectory = Directory.CreateTempSubdirectory("osu-initial-images-");
        var imageBytes = Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAusB9Wl2nC8AAAAASUVORK5CYII=");
        var entries = Enumerable.Range(0, HomePageVM.InitialSongCount).Select(index =>
        {
            var path = Path.Combine(imageDirectory.FullName, $"{index:D2}.png");
            File.WriteAllBytes(path, imageBytes);
            return new FolderEntry($@"C:\Songs\{index:D2}", index.ToString(), DateTimeOffset.UnixEpoch.AddMinutes(index))
            {
                DisplayName = index.ToString(),
                BackgroundPath = index == HomePageVM.InitialSongCount - 1
                    ? Path.Combine(imageDirectory.FullName, "missing.png")
                    : path
            };
        }).ToArray();
        var validPaths = entries.Select(entry => entry.BackgroundPath).Where(File.Exists).ToArray();
        var vm = CreateVM(new StubHomeFolderService
        {
            Songs = (_, _) => Task.FromResult<FolderSnapshot?>(new FolderSnapshot(entries))
        });
        var page = new HomePage(vm);
        var window = new Window
        {
            Content = page,
            Width = 1280,
            Height = 720,
            ShowInTaskbar = false,
            WindowStyle = WindowStyle.None
        };
        window.Show();
        try
        {
            UiTestPump.Run(page.LoadAsync(CancellationToken.None));
            var songsList = (ItemsControl)page.FindName("SongsList");
            PumpUntil(() => Math.Abs(songsList.Opacity - 1) < 0.001);
            foreach (var path in validPaths) File.Delete(path);
            var decodeWidth = (int)Math.Ceiling(336 * VisualTreeHelper.GetDpi(songsList).DpiScaleX);
            var cachedLoads = Task.WhenAll(validPaths.Select(path =>
                page.ImageLoader.LoadAsync(path, decodeWidth, CancellationToken.None)));
            UiTestPump.Run(cachedLoads);
            var missingLoad = page.ImageLoader.LoadAsync(
                Path.Combine(imageDirectory.FullName, "missing.png"), decodeWidth, CancellationToken.None);
            UiTestPump.Run(missingLoad);

            Assert.Multiple(() =>
            {
                Assert.That(vm.Songs, Has.Count.EqualTo(HomePageVM.InitialSongCount));
                Assert.That(cachedLoads.Result, Has.All.Not.Null);
                Assert.That(missingLoad.Result, Is.Null);
                Assert.That(songsList.Opacity, Is.EqualTo(1).Within(0.001));
            });
        }
        finally
        {
            window.Close();
            imageDirectory.Delete(true);
        }
    }

    [Test]
    public void RetryHidesSongsThenRevealsCurrentReplacementGeneration()
    {
        EnsureResources();
        var image = CreateTestImage();
        FolderSnapshot Snapshot(string prefix) => new(Enumerable.Range(0, HomePageVM.InitialSongCount).Select(index =>
            new FolderEntry($@"C:\Songs\{prefix}{index}", $"{prefix}{index}", DateTimeOffset.UnixEpoch)
            {
                BackgroundPath = image
            }));
        var pending = new TaskCompletionSource<FolderSnapshot?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var folders = new StubHomeFolderService
        {
            Songs = (_, _) => Task.FromResult<FolderSnapshot?>(Snapshot("Initial"))
        };
        var vm = CreateVM(folders);
        var page = new HomePage(vm);
        var window = new Window { Content = page, Width = 1280, Height = 720, ShowInTaskbar = false };
        window.Show();
        try
        {
            UiTestPump.Run(page.LoadAsync(CancellationToken.None));
            var songsList = (ItemsControl)page.FindName("SongsList");
            PumpUntil(() => Math.Abs(songsList.Opacity - 1) < 0.001);
            vm.ReportDemandFailure(new IOException("retry"));
            folders.Songs = (_, token) => pending.Task.WaitAsync(token);

            var retry = vm.RetrySongsCommand.ExecuteAsync(null);
            PumpUntil(() => vm.IsProcessing);
            Assert.That(songsList.Opacity, Is.Zero);
            pending.SetResult(Snapshot("Replacement"));
            UiTestPump.Run(retry);
            PumpUntil(() => Math.Abs(songsList.Opacity - 1) < 0.001);

            Assert.Multiple(() =>
            {
                Assert.That(vm.Songs, Has.Count.EqualTo(HomePageVM.InitialSongCount));
                Assert.That(vm.Songs.Select(song => song.Name), Has.All.StartsWith("Replacement"));
                Assert.That(vm.IsProcessing, Is.False);
            });
        }
        finally
        {
            window.Close();
            Directory.Delete(Path.GetDirectoryName(image)!, true);
        }
    }

    [Test]
    public void DeactivationDuringRetryPreventsLateReveal()
    {
        EnsureResources();
        var image = CreateTestImage();
        var snapshot = new FolderSnapshot(Enumerable.Range(0, HomePageVM.InitialSongCount).Select(index =>
            new FolderEntry($@"C:\Songs\{index}", index.ToString(), DateTimeOffset.UnixEpoch)
            {
                BackgroundPath = image
            }));
        var pending = new TaskCompletionSource<FolderSnapshot?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var folders = new StubHomeFolderService
        {
            Songs = (_, _) => Task.FromResult<FolderSnapshot?>(snapshot)
        };
        var vm = CreateVM(folders);
        var page = new HomePage(vm);
        var window = new Window { Content = page, Width = 1280, Height = 720, ShowInTaskbar = false };
        window.Show();
        try
        {
            UiTestPump.Run(page.LoadAsync(CancellationToken.None));
            var songsList = (ItemsControl)page.FindName("SongsList");
            PumpUntil(() => Math.Abs(songsList.Opacity - 1) < 0.001);
            vm.ReportDemandFailure(new IOException("retry"));
            folders.Songs = (_, token) => pending.Task.WaitAsync(token);
            var retry = vm.RetrySongsCommand.ExecuteAsync(null);
            PumpUntil(() => vm.IsProcessing);

            page.Deactivate();
            pending.TrySetResult(snapshot);
            UiTestPump.Run(retry);
            window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);

            Assert.That(songsList.Opacity, Is.Zero);
        }
        finally
        {
            window.Close();
            Directory.Delete(Path.GetDirectoryName(image)!, true);
        }
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

    private static string CreateTestImage()
    {
        var directory = Directory.CreateTempSubdirectory("osu-card-image-");
        var path = Path.Combine(directory.FullName, "background.png");
        File.WriteAllBytes(path, Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAusB9Wl2nC8AAAAASUVORK5CYII="));
        return path;
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

    private static void PumpFor(TimeSpan duration)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer(duration, DispatcherPriority.Background,
            (_, _) => frame.Continue = false, Dispatcher.CurrentDispatcher);
        Dispatcher.PushFrame(frame);
        timer.Stop();
    }

    private static HomePageVM CreateVM(IHomeFolderService folders) =>
        new(folders, new StubAppSettingsService(), new StubInstallationDialogService(),
            new StubProjectCreationDialogService(), new ImmediateUiScheduler(),
            new StubProjectFileService((_, _, _) => Task.FromResult("saved.json")), new WeakReferenceMessenger());
}
