using CommunityToolkit.Mvvm.Messaging;
using Hitbench.Services.FileSystem;
using System.Diagnostics;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using Hitbench.Core.Entities;
using Hitbench.Resources.DataModels;
using Hitbench.Resources.CustomControls;
using Hitbench.Resources.Messages;
using Hitbench.ViewModels.Shell;
using Hitbench.ViewModels.Shell.HomePage;
using Hitbench.Views.Pages;

namespace Hitbench.Test.Presentation;

[Apartment(ApartmentState.STA)]
[NonParallelizable]
public class HomePageIntegrationTests
{
    [Test]
    public void EmptyProjectsKeepNavigationAvailable()
    {
        EnsureResources();
        var vm = CreateVM(new StubHomeFolderService { Projects = _ => Task.FromResult(new FolderSnapshot([])) });
        var page = new HomePage(vm);
        using var view = new InMemoryView(new Window { Content = page, Width = 720, Height = 520 });
        UiTestPump.Run(page.LoadAsync(CancellationToken.None));
        view.UpdateLayout();
        Assert.That(((ItemsControl)page.FindName("ProjectsList")).Items.OfType<OHsHomeProjectListItem>().Where(item => !item.IsHeading), Is.Empty);
        Assert.That(vm.OpenSongSearchCommand.CanExecute(null), Is.True);
    }

    [Test]
    public void PersistentSongSearchBindsFiltersAndClearsWithEscape()
    {
        EnsureResources();
        var vm = CreateVM(new StubHomeFolderService
        {
            Songs = (_, _) => Task.FromResult<FolderSnapshot?>(new FolderSnapshot([
                new FolderEntry("one", "one", DateTimeOffset.MinValue),
                new FolderEntry("two", "two", DateTimeOffset.MinValue)]))
        });
        var page = new HomePage(vm);
        var window = new Window { Content = page, Width = 1280, Height = 720 };
        using var view = new InMemoryView(window);
        UiTestPump.Run(page.LoadAsync(CancellationToken.None));
        var input = (TextBox)page.FindName("SongSearchTextBox");
        Assert.That(input.IsVisible, Is.True);
        Assert.That(AutomationProperties.GetName(input), Is.EqualTo("Search mapsets"));
        input.Text = "two";
        UiTestPump.Run(vm.SearchSongsCommand.ExecutionTask!);
        PumpUntil(() => !vm.IsAwaitingSongsPresentation);
        Assert.That(vm.Songs.Single().Name, Is.EqualTo("two"));
        input.RaiseEvent(new System.Windows.Input.KeyEventArgs(System.Windows.Input.Keyboard.PrimaryDevice,
            PresentationSource.FromVisual(input)!, Environment.TickCount, System.Windows.Input.Key.Escape)
        { RoutedEvent = System.Windows.Input.Keyboard.PreviewKeyDownEvent });
        UiTestPump.Run(vm.SearchSongsCommand.ExecutionTask!);
        view.UpdateLayout();
        Assert.That(vm.SongSearchText, Is.Empty);
        Assert.That(vm.IsSongSearchExpanded, Is.False);
        Assert.That(input.IsVisible, Is.True);
        window.Close();
    }

    [Test]
    public void HomeReloadGestureExecutesForcedReloadFromSearchContext()
    {
        EnsureResources();
        var vm = CreateVM(new StubHomeFolderService
        {
            Songs = (_, _) => Task.FromResult<FolderSnapshot?>(new FolderSnapshot([new FolderEntry("old", "old", DateTimeOffset.MinValue)])),
            ReloadSongs = (_, _) => Task.FromResult<FolderSnapshot?>(new FolderSnapshot([new FolderEntry("new", "new", DateTimeOffset.UtcNow)]))
        });
        var page = new HomePage(vm);
        var window = new Window { Content = page, Width = 1280, Height = 720 };
        using var view = new InMemoryView(window);
        UiTestPump.Run(page.LoadAsync(CancellationToken.None));
        var binding = page.InputBindings.OfType<System.Windows.Input.KeyBinding>().SingleOrDefault(x =>
            x.Key == System.Windows.Input.Key.R && x.Modifiers == System.Windows.Input.ModifierKeys.Control);
        Assert.That(binding, Is.Not.Null, "Home must bind Ctrl+R.");
        vm.OpenSongSearchCommand.Execute(null);
        view.UpdateLayout();
        Assert.That(binding!.Command.CanExecute(null), Is.True);
        binding.Command.Execute(null);
        UiTestPump.Run(vm.ReloadSongsCommand.ExecutionTask!);
        PumpUntil(() => !vm.IsAwaitingSongsPresentation);
        Assert.That(vm.Songs.Single().Name, Is.EqualTo("new"));
        Assert.That(vm.IsSongSearchExpanded, Is.True);
        window.Close();
    }

    [Test]
    public void ReloadInvalidatesArtworkAtSamePath()
    {
        EnsureResources();
        var path = CreateTestImage();
        var folders = new StubHomeFolderService
        {
            Songs = (_, _) => Task.FromResult<FolderSnapshot?>(new FolderSnapshot([
                new FolderEntry("one", "one", DateTimeOffset.MinValue) { BackgroundPath = path }]))
        };
        var vm = CreateVM(folders);
        var page = new HomePage(vm);
        var window = new Window { Content = page, Width = 1280, Height = 720 };
        using var view = new InMemoryView(window);
        try
        {
            UiTestPump.Run(page.LoadAsync(CancellationToken.None));
            var original = Descendants<Image>(page).Single().Source;
            Assert.That(original, Is.Not.Null);
            var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
            encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(
                System.Windows.Media.Imaging.BitmapSource.Create(2, 1, 96, 96, PixelFormats.Bgra32,
                    null, new byte[] { 0, 0, 255, 255, 0, 255, 0, 255 }, 8)));
            using (var stream = File.Create(path)) encoder.Save(stream);
            UiTestPump.Run(vm.ReloadSongsCommand.ExecuteAsync(null));
            PumpUntil(() => !vm.IsAwaitingSongsPresentation);
            var refreshed = (System.Windows.Media.Imaging.BitmapSource)Descendants<Image>(page).Single().Source;
            Assert.That(refreshed, Is.Not.SameAs(original));
            Assert.That(refreshed.PixelHeight * 2, Is.EqualTo(refreshed.PixelWidth));
            var converted = new System.Windows.Media.Imaging.FormatConvertedBitmap(refreshed, PixelFormats.Bgra32, null, 0);
            var pixels = new byte[converted.PixelWidth * converted.PixelHeight * 4];
            converted.CopyPixels(pixels, converted.PixelWidth * 4, 0);
            Assert.That(pixels[2], Is.EqualTo(255), "Reload must decode edited red pixel.");
        }
        finally { window.Close(); Directory.Delete(Path.GetDirectoryName(path)!, true); }
    }
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
        var window = TestShell.Create(new HomePage(vm), new WeakReferenceMessenger(),
            WorkstationTestPage.Create, vm, new StubAppSettingsService());
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
        var window = TestShell.Create(new HomePage(vm), new WeakReferenceMessenger(),
            WorkstationTestPage.Create, vm, settings);
        window.ApplySettings(new AppSettings());
        using var view = new InMemoryView(window);
        window.Width = 1024;
        window.Height = 640;
        window.RecordWindowSize(1024, 640);

        window.Close();
        window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);

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
        var requests = new List<WindowState>();
        var window = TestShell.Create(new HomePage(vm), new WeakReferenceMessenger(),
            WorkstationTestPage.Create, vm, settings, state => requests.Add(state));
        window.ApplySettings(new AppSettings { RestoredWidth = 1024, RestoredHeight = 640 });
        using var view = new InMemoryView(window);
        try
        {
            var maximize = (BalloonButton)((Hitbench.Views.Shell.OHsShellHeader)window.FindName("ShellHeader")).FindName("MaximizeRestoreButton");
            var minimize = (BalloonButton)((Hitbench.Views.Shell.OHsShellHeader)window.FindName("ShellHeader")).FindName("MinimizeButton");

            maximize.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
            Assert.That(requests, Is.EqualTo(new[] { WindowState.Maximized }));
            window.WindowState = WindowState.Maximized;
            window.RecordWindowState(1920, 1080);
            minimize.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
            Assert.That(requests, Is.EqualTo(new[] { WindowState.Maximized, WindowState.Minimized }));
            window.WindowState = WindowState.Minimized;
            window.RecordWindowState(0, 0);
        }
        finally { window.Close(); }

        window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
        Assert.That(saved, Is.EqualTo((true, 1024d, 640d)));
    }

    [Test]
    public void ShellCaptionActionsUseBalloonButtonsWithSemanticIconsAndNames()
    {
        EnsureResources();
        var vm = CreateVM(new StubHomeFolderService());
        var window = TestShell.Create(new HomePage(vm), new WeakReferenceMessenger(),
            WorkstationTestPage.Create, vm, new StubAppSettingsService());
        try
        {
            var minimize = (BalloonButton)((Hitbench.Views.Shell.OHsShellHeader)window.FindName("ShellHeader")).FindName("MinimizeButton");
            var maximize = (BalloonButton)((Hitbench.Views.Shell.OHsShellHeader)window.FindName("ShellHeader")).FindName("MaximizeRestoreButton");
            var close = (BalloonButton)((Hitbench.Views.Shell.OHsShellHeader)window.FindName("ShellHeader")).FindName("CloseButton");

            Assert.Multiple(() =>
            {
                Assert.That(minimize.ButtonText, Is.EqualTo("Minimize"));
                Assert.That(AutomationProperties.GetName(minimize), Is.EqualTo("Minimize"));
                Assert.That(maximize.ButtonText, Is.EqualTo("Maximize"));
                Assert.That(AutomationProperties.GetName(maximize), Is.EqualTo("Maximize"));
                Assert.That(close.ButtonText, Is.EqualTo("Close"));
                Assert.That(AutomationProperties.GetName(close), Is.EqualTo("Close"));
                Assert.That(close.Style, Is.SameAs(window.FindResource("OHsBalloonDestructiveActionStyle")));
            });

            window.ApplySettings(new AppSettings { IsMaximized = true });
            Assert.Multiple(() =>
            {
                Assert.That(maximize.ButtonText, Is.EqualTo("Restore"));
                Assert.That(AutomationProperties.GetName(maximize), Is.EqualTo("Restore"));
                Assert.That(maximize.ToolTip, Is.EqualTo("Restore"));
            });
        }
        finally { window.Close(); }
    }

    [Test]
    public void HomeRetryActionsUseBalloonButtonsWithPersistentTextAndIcons()
    {
        EnsureResources();
        var vm = CreateVM(new StubHomeFolderService());
        var page = new HomePage(vm);
        var window = new Window { Content = page, ShowInTaskbar = false };
        using var view = new InMemoryView(window);
        try
        {
            var projects = (BalloonButton)page.FindName("ProjectsRetryButton");
            var songs = (BalloonButton)page.FindName("SongsRetryButton");

            Assert.Multiple(() =>
            {
                Assert.That(projects.ButtonText, Is.EqualTo("Retry Projects"));
                Assert.That(projects.Command, Is.SameAs(vm.RetryProjectsCommand));
                Assert.That(AutomationProperties.GetName(projects), Is.EqualTo("Retry Projects"));
                Assert.That(songs.ButtonText, Is.EqualTo("Refresh Songs"));
                Assert.That(songs.Command, Is.SameAs(vm.RetrySongsCommand));
                Assert.That(AutomationProperties.GetName(songs), Is.EqualTo("Refresh Songs"));
            });
        }
        finally { window.Close(); }
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
        var window = TestShell.Create(new HomePage(vm), messenger, WorkstationTestPage.Create, vm,
            new StubAppSettingsService());
        using var view = new InMemoryView(window);
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
        var window = TestShell.Create(new HomePage(vm), new WeakReferenceMessenger(), WorkstationTestPage.Create, vm,
            new StubAppSettingsService());
        using var view = new InMemoryView(window);
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
        using var view = new InMemoryView(window);
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
        using var view = new InMemoryView(window);
        page.UpdateSongsLayout(new Size(1920, 1040));
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
        var page = (HomePage)((ContentControl)window.FindName("PageHost")).Content;
        Assert.That(window.DataContext, Is.SameAs(page.DataContext));
        var fresh = provider.GetRequiredService<Func<HomePage>>()();
        Assert.That(fresh, Is.Not.SameAs(page));
        Assert.That(fresh.DataContext, Is.Not.SameAs(page.DataContext));
        fresh.Deactivate();
        window.Close();
    }

    private static void EnsureResources()
    {
        var app = Application.Current ?? new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        app.Resources.MergedDictionaries.Clear();
        UiTestPump.EnsureFontResources(app);
        foreach (var resource in new[] { "Themes/Dark.xaml", "Themes/Tokens.xaml", "Converters/Converters.xaml", "Icons.xaml", "Styles/TextBlock.xaml", "Styles/ItemsControl.xaml", "Styles/StackPanel.xaml", "Styles/Image.xaml", "Styles/ColumnDefinition.xaml", "Styles/Grid.xaml", "Styles/Border.xaml", "Styles/ContentPresenter.xaml", "Styles/Button.xaml", "Styles/MenuItem.xaml", "Styles/ContextMenu.xaml", "Styles/BalloonComboBox.xaml", "Styles/SegmentedToggleGroup.xaml", "Styles/SegmentedToggleOption.xaml", "Styles/ToolTip.xaml", "Styles/Window.xaml", "Styles/ScrollViewer.xaml", "Styles/TextBox.xaml", "Styles/ComboBox.xaml", "Styles/ListBox.xaml", "Styles/Slider.xaml" })
            app.Resources.MergedDictionaries.Add(new ResourceDictionary
            {
                Source = new Uri($"/Hitbench;component/Resources/{resource}", UriKind.Relative)
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
        var window = TestShell.Create(page, messenger, () => { creations++; return WorkstationTestPage.Create(); }, vm,
            new StubAppSettingsService());
        using var view = new InMemoryView(window);
        try
        {
            UiTestPump.Run(window.LoadAsync());
            var host = (ContentControl)window.FindName("PageHost");
            Assert.That(host.Content, Is.SameAs(page));
            Assert.That(((FrameworkElement)((Hitbench.Views.Shell.OHsShellHeader)window.FindName("ShellHeader")).FindName("WorkstationActions")).Visibility, Is.EqualTo(Visibility.Collapsed));
            Assert.That(creations, Is.Zero);
            messenger.Send(new ProjectCreatedMessage("saved.json"));
            Assert.That(host.Content, Is.TypeOf<WorkstationPage>());
            Assert.That(((FrameworkElement)((Hitbench.Views.Shell.OHsShellHeader)window.FindName("ShellHeader")).FindName("WorkstationActions")).Visibility, Is.EqualTo(Visibility.Visible));
            Assert.That(host.IsEnabled, Is.True);
            Assert.That(creations, Is.EqualTo(1));
        }
        finally { window.Close(); }
        Assert.That(messenger.IsRegistered<ProjectCreatedMessage>(window), Is.False);
        Assert.That(messenger.IsRegistered<ProjectOpenRequestedMessage>(window), Is.False);
        Assert.That(messenger.IsRegistered<ProjectCreationFailedMessage>(window), Is.False);
        messenger.Send(new ProjectCreatedMessage("late.json"));
        messenger.Send(new ProjectOpenRequestedMessage("late-open.json"));
        Assert.That(creations, Is.EqualTo(1));
    }

    [Test]
    public void OpenRequestLoadsSelectedProjectOnlyOnce()
    {
        EnsureResources();
        var messenger = new WeakReferenceMessenger();
        var vm = CreateVM(new StubHomeFolderService());
        var source = new StubWorkstationSourceService();
        string? loadedPath = null;
        source.Load = (path, _) =>
        {
            loadedPath = path;
            return Task.FromResult(new WorkstationSource("map.osu", "audio.wav", new ProjectData()));
        };
        var creations = 0;
        var window = TestShell.Create(new HomePage(vm), messenger, () =>
        {
            creations++;
            return new WorkstationPage(new WorkstationPageVM(
                source, new Hitbench.Services.MarkerPlaybackCoordinator(new StubWorkstationAudioService(), (duration, token) => Task.Delay(duration, token)), new ImmediateUiScheduler()));
        }, vm, new StubAppSettingsService());
        using var view = new InMemoryView(window);
        try
        {
            messenger.Send(new ProjectOpenRequestedMessage(@"C:\App\projects\Existing\project.json"));
            view.UpdateLayout();
            var workstation = (WorkstationPage)((ContentControl)window.FindName("PageHost")).Content;
            UiTestPump.Run(workstation.LoadCompletion);

            messenger.Send(new ProjectOpenRequestedMessage(@"C:\App\projects\Other\project.json"));

            Assert.Multiple(() =>
            {
                Assert.That(loadedPath, Is.EqualTo(@"C:\App\projects\Existing\project.json"));
                Assert.That(creations, Is.EqualTo(1));
            });
        }
        finally { window.Close(); }
    }

    [Test]
    public void ShellFailuresUseSubstitutedNotificationAndIgnoreMessagesAfterClose()
    {
        EnsureResources();
        var errors = new List<string>();
        var messenger = new WeakReferenceMessenger();
        var vm = CreateVM(new StubHomeFolderService());
        var window = new MainWindow(new HomePage(vm), messenger,
            () => throw new IOException("Navigation failed"), vm, new StubAppSettingsService(), errors.Add);
        try
        {
            messenger.Send(new ProjectCreationFailedMessage("Save failed"));
            messenger.Send(new ProjectOpenRequestedMessage("existing.json"));
            messenger.Send(new ProjectCreatedMessage("saved.json"));
            Assert.That(errors, Is.EqualTo(new[]
            {
                "Save failed",
                "Unable to open workstation: Navigation failed",
                "Unable to open workstation: Navigation failed"
            }));
        }
        finally { window.Close(); }
        messenger.Send(new ProjectCreationFailedMessage("Late failure"));
        Assert.That(errors, Has.Count.EqualTo(3));
    }

    [Test]
    public void ClosingShellCancelsProjectCreation()
    {
        EnsureResources();
        var pending = new TaskCompletionSource<string>();
        var messenger = new WeakReferenceMessenger();
        var vm = new HomePageVM(new StubHomeFolderService(_ => Task.FromResult(new HomeFolderData([], [], "C:\\osu", true, false))),
            new StubAppSettingsService(), new StubInstallationDialogService(),
            new ImmediateUiScheduler(),
            new StubProjectFileService((_, _, token) => pending.Task.WaitAsync(token)), messenger);
        var window = TestShell.Create(new HomePage(vm), messenger,
            () => throw new AssertionException("Must not navigate"), vm, new StubAppSettingsService());
        using var view = new InMemoryView(window);
        vm.SelectedMapset = new FolderEntry("C:\\Songs\\Selected", "Selected", DateTimeOffset.MinValue);
        UiTestPump.Run(vm.ShowProjectCreationCommand.ExecuteAsync(null));
        vm.DraftProjectName = "Selected";
        var operation = vm.CreateProjectCommand.ExecuteAsync(null);
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
    public void SongButtonSelectsFolderAndInlineCreateDisablesWhileSaving()
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
            new ImmediateUiScheduler(),
            new StubProjectFileService((folder, _, _) => { selected = folder; return pending.Task; }), messenger);
        var page = new HomePage(vm);
        var window = TestShell.Create(page, messenger, WorkstationTestPage.Create, vm,
            new StubAppSettingsService());
        using var view = new InMemoryView(window);
        try
        {
            UiTestPump.Run(window.LoadAsync());
            PumpUntil(() => vm.Songs.Count == 1);
            var button = Descendants<Button>(page).Single(button => button.IsVisible && button.DataContext is FolderEntry);
            var text = Descendants<TextBlock>(button).Where(t => t.IsVisible).ToArray();
            var backgroundControl = Descendants<Image>(button).Single();
            Assert.That(backgroundControl.Source, Is.Not.Null, "Initial background must be ready when loading completes.");
            Assert.That(text.Select(value => value.Text), Is.EquivalentTo(new[] { "Artist", "Title" }));
            UiControlContract.AssertStyle<Button>(button, "HomePageSongCardButtonStyle");
            UiControlContract.AssertStyle<Image>(backgroundControl, "HomePageSongBackgroundStyle");
            button.RaiseEvent(new System.Windows.Input.MouseEventArgs(System.Windows.Input.Mouse.PrimaryDevice, 0)
            { RoutedEvent = UIElement.MouseEnterEvent });
            view.UpdateLayout();
            Assert.That(button.IsTabStop, Is.False);
            Assert.That(button.CommandParameter, Is.SameAs(row));
            Assert.That(vm.FatalError, Is.Null);
            Assert.That(button.IsEnabled, Is.True);
            var peer = new ButtonAutomationPeer(button);
            Assert.That(peer.GetName(), Is.EqualTo("Artist - Title"));
            button.Command!.Execute(button.CommandParameter);
            window.Dispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
            Assert.That(vm.SelectedMapset, Is.SameAs(row));
            Assert.That(selected, Is.Null);
            UiTestPump.Run(vm.ShowProjectCreationCommand.ExecuteAsync(null));
            vm.DraftProjectName = "Selected";
            var creating = vm.CreateProjectCommand.ExecuteAsync(null);
            view.UpdateLayout();
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

    [TestCase("Dark", FlowDirection.LeftToRight)]
    [TestCase("Dark", FlowDirection.RightToLeft)]
    [TestCase("Light", FlowDirection.LeftToRight)]
    [TestCase("Light", FlowDirection.RightToLeft)]
    public void ProjectCardKeepsStyleAndCommandAcrossCosmeticChanges(string theme, FlowDirection direction)
    {
        EnsureResources();
        Application.Current.Resources.MergedDictionaries[0].Source =
            new Uri($"/Hitbench;component/Resources/Themes/{theme}.xaml", UriKind.Relative);
        var row = new FolderEntry(
            @"C:\App\projects\A project with a long localized folder path that must wrap",
            "A project with a long localized name that must wrap", DateTimeOffset.MinValue);
        var folders = new StubHomeFolderService
        {
            Projects = _ => Task.FromResult(new FolderSnapshot([row]))
        };
        var messenger = new WeakReferenceMessenger();
        var opened = new List<string>();
        messenger.Register<ProjectOpenRequestedMessage>(this,
            (_, message) => opened.Add(message.ProjectPath));
        var vm = new HomePageVM(folders, new StubAppSettingsService(), new StubInstallationDialogService(),
            new ImmediateUiScheduler(),
            new StubProjectFileService((_, _, _) => Task.FromResult("saved.json")), messenger);
        var page = new HomePage(vm);
        var window = new Window
        {
            Content = page,
            Width = 720,
            Height = 520,
            FlowDirection = direction,
            ShowInTaskbar = false
        };
        using var view = new InMemoryView(window);
        try
        {
            UiTestPump.Run(page.LoadAsync(CancellationToken.None));
            view.UpdateLayout();
            var projects = (ItemsControl)page.FindName("ProjectsList");
            var button = Descendants<Button>(projects).Single(value => value.Command == vm.OpenProjectCommand);
            Assert.That(Hitbench.Resources.Helpers.OHsFolderActionPresentation.GetIsPressOffsetEnabled(button), Is.False);
            Assert.That(Hitbench.Resources.Helpers.OHsFolderActionPresentation.GetIsPressOffsetEnabled(new Button()), Is.True);
            var card = Descendants<ContentControl>(projects).Single(value => value.ContentTemplate == page.FindResource("OHsHomePageProjectRowTemplate"));
            var presenter = Descendants<ContentPresenter>(card).First(value => value.ContentTemplate == card.ContentTemplate);
            var border = (Border)VisualTreeHelper.GetChild(presenter, 0);
            UiControlContract.AssertStyle<Border>(border, "OHsHomePageProjectCardBorderStyle");
            Assert.That(Descendants<BalloonButton>(projects).Count(value => value.Command == vm.OpenProjectCommand), Is.Zero);
            var trigger = Descendants<BalloonComboBox>(projects).Single();
            UiControlContract.AssertStyle<BalloonComboBox>(trigger, "OHsHomePageMenuTriggerStyle");
            var menu = trigger.ContextMenu;
            menu.DataContext = row;
            var pin = (MenuItem)menu.Items[0];
            var delete = (MenuItem)menu.Items[1];
            Assert.That(pin.Command, Is.SameAs(vm.ToggleProjectPinCommand));
            Assert.That(delete.Command, Is.SameAs(vm.DeleteProjectCommand));
            Assert.That(pin.CommandParameter, Is.SameAs(row));
            UiTestPump.Run(vm.ToggleProjectPinCommand.ExecuteAsync(row));
            Assert.That(opened, Is.Empty);
            var name = Descendants<TextBlock>(button).Single(block =>
                ReferenceEquals(block.Style, page.FindResource("HomePageProjectNameStyle")));
            button.Width = 280;
            button.Height = 90;
            button.Margin = new Thickness(7);
            button.Padding = new Thickness(5);
            button.Background = Brushes.Magenta;
            button.Foreground = Brushes.Green;
            name.FontSize = 17;
            view.UpdateLayout();
            UiControlContract.AssertStyle<Button>(button, "OHsHomePageProjectContentButtonStyle");
            UiControlContract.AssertStyle<TextBlock>(name, "HomePageProjectNameStyle");
            var originalStyle = button.Style;
            button.Style = (Style)page.FindResource("BaseButtonStyle");
            Assert.Throws<AssertionException>(() => UiControlContract.AssertStyle<Button>(button, "OHsHomePageProjectContentButtonStyle"));
            button.Style = originalStyle;
            Assert.Throws<AssertionException>(() => UiControlContract.AssertStyle<TextBox>(button, "OHsHomePageProjectContentButtonStyle"));
            var peer = new ButtonAutomationPeer(button);

            ((IInvokeProvider)peer.GetPattern(PatternInterface.Invoke)).Invoke();
            window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
            vm.IsInteractionBlocked = true;
            window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);

            Assert.Multiple(() =>
            {
                Assert.That(AutomationProperties.GetName(button), Is.EqualTo(row.Name));
                Assert.That(AutomationProperties.GetHelpText(button), Is.EqualTo(row.FullName));
                Assert.That(button.CommandParameter, Is.SameAs(row));
                Assert.That(button.IsEnabled, Is.False);
                Assert.That(opened, Is.EqualTo(new[] { Path.Combine(row.FullName, "project.json") }));
                Assert.That(name.Text, Is.EqualTo(row.Name));
                UiControlContract.AssertStyle<Button>(button, "OHsHomePageProjectContentButtonStyle");
                UiControlContract.AssertStyle<TextBlock>(name, "HomePageProjectNameStyle");
                Assert.That(VirtualizingPanel.GetIsVirtualizing(projects), Is.True);
                Assert.That(VirtualizingPanel.GetVirtualizationMode(projects), Is.EqualTo(VirtualizationMode.Recycling));
            });
        }
        finally { window.Close(); }
    }

    [Test]
    public void SongCardsKeepAssignedStylesAndVirtualizationAcrossSizesAndThemes()
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
                foreach (var resource in new[] { $"Themes/{theme}.xaml", "Themes/Tokens.xaml", "Converters/Converters.xaml", "Icons.xaml", "Styles/TextBlock.xaml", "Styles/ItemsControl.xaml", "Styles/StackPanel.xaml", "Styles/Image.xaml", "Styles/ColumnDefinition.xaml", "Styles/Grid.xaml", "Styles/Border.xaml", "Styles/ContentPresenter.xaml", "Styles/Button.xaml", "Styles/MenuItem.xaml", "Styles/ContextMenu.xaml", "Styles/BalloonComboBox.xaml", "Styles/SegmentedToggleGroup.xaml", "Styles/SegmentedToggleOption.xaml", "Styles/ToolTip.xaml", "Styles/Window.xaml", "Styles/TextBox.xaml", "Styles/ComboBox.xaml", "Styles/ListBox.xaml", "Styles/Slider.xaml" })
                    app.Resources.MergedDictionaries.Add(new ResourceDictionary
                    {
                        Source = new Uri($"/Hitbench;component/Resources/{resource}", UriKind.Relative)
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
                    using var view = new InMemoryView(window);
                    page.UpdateSongsLayout(new Size(1920, 1080));
                    UiTestPump.Run(page.LoadAsync(CancellationToken.None));
                    PumpUntil(() => vm.Songs.Count == 45);
                    view.UpdateLayout();
                    TestContext.Progress.WriteLine($"Theme={theme} StubFolders=2000 HomePageFirstActionableRowMs={firstRowTimer.Elapsed.TotalMilliseconds:F2}");

                    var songsList = (ItemsControl)page.FindName("SongsList");
                    var panel = Descendants<SpacedUniformGrid>(songsList).Single();
                    var button = Descendants<Button>(songsList)
                        .First(candidate => candidate.DataContext is FolderEntry);
                    var labels = Descendants<TextBlock>(button).Where(label => label.IsVisible).ToArray();
                    var backgroundImage = Descendants<Image>(button).Single();
                    UiControlContract.AssertStyle<Button>(button, "HomePageSongCardButtonStyle");
                    UiControlContract.AssertStyle<Image>(backgroundImage, "HomePageSongBackgroundStyle");
                    UiControlContract.AssertStyle<Grid>((Grid)button.Content, "HomePageSongArtworkStyle");
                    UiControlContract.AssertStyle<TextBlock>(labels.Single(label => label.Text == "Artist"), "HomePageSongArtistStyle");
                    UiControlContract.AssertStyle<TextBlock>(labels.Single(label => label.Text.StartsWith("Title")), "HomePageSongArtistStyle");
                    var peer = new ButtonAutomationPeer(button);
                    Assert.Multiple(() =>
                    {
                        Assert.That(((ItemsControl)page.FindName("ProjectsList")).Items.OfType<OHsHomeProjectListItem>().Count(item => !item.IsHeading), Is.EqualTo(1));
                        Assert.That(songsList.Items.Count, Is.EqualTo(45));
                        Assert.That(panel.ExtentWidth, Is.EqualTo(panel.ViewportWidth).Within(.01));
                        Assert.That(panel.Children.Count, Is.LessThanOrEqualTo((panel.VisibleRowCount + 3) * panel.Columns));
                        Assert.That(labels.Select(label => label.Text), Is.EquivalentTo(new[]
                            { "Artist", "Title " + new string('x', 80) }));
                        Assert.That(peer.GetName(), Is.EqualTo("Artist - Title"));
                        Assert.That(peer.GetHelpText(), Does.Contain("long folder name"));
                    });

                    window.FlowDirection = FlowDirection.RightToLeft;
                    window.Width = page.MinWidth;
                    window.Height = page.MinHeight;
                    view.UpdateLayout();
                    window.Dispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
                    var scroll = Descendants<ScrollViewer>(songsList).Single();
                    Assert.Multiple(() =>
                    {
                        Assert.That(scroll.ScrollableWidth, Is.Zero);
                        Assert.That(scroll.ScrollableHeight, Is.GreaterThan(0));
                        Assert.That(panel.ExtentWidth, Is.EqualTo(panel.ViewportWidth).Within(.01));
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
    public void SongsLoadOnlyAfterInitialPresentationAndGenuineThresholdScrolls()
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
        using var view = new InMemoryView(window);
        try
        {
            var songsList = (ItemsControl)page.FindName("SongsList");
            Assert.That(vm.Songs, Is.Empty);
            page.UpdateSongsLayout(new Size(1920, 1080));
            UiTestPump.Run(page.LoadAsync(CancellationToken.None));
            PumpUntil(() => vm.Songs.Count == HomePageVM.InitialSongCount);
            PumpUntil(() => !vm.IsAwaitingSongsPresentation);
            var panel = Descendants<SpacedUniformGrid>(songsList).Single();
            var scroll = Descendants<ScrollViewer>(songsList).Single();

            window.Width = 1900;
            view.UpdateLayout();
            window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
            Assert.That(vm.Songs, Has.Count.EqualTo(45), "Resize/layout must not count as user scrolling.");

            var batch = panel.ItemsPerViewport;
            var expected = HomePageVM.InitialSongCount + batch;
            scroll.ScrollToVerticalOffset(Math.Max(0, scroll.ScrollableHeight - panel.RowStride / 2));
            PumpUntil(() => vm.Songs.Count == expected);

            scroll.ScrollToEnd();
            scroll.ScrollToVerticalOffset(Math.Max(0, scroll.ScrollableHeight - 1));
            scroll.ScrollToEnd();
            expected += batch;
            PumpUntil(() => vm.Songs.Count == expected);
            window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
            Assert.That(vm.Songs, Has.Count.EqualTo(expected), "Repeated notifications must coalesce to one batch.");

            scroll.ScrollToVerticalOffset(Math.Max(0, scroll.ScrollableHeight - panel.RowStride * 2));
            view.UpdateLayout();
            window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
            Assert.That(vm.Songs, Has.Count.EqualTo(expected));
            scroll.LineDown();
            expected += batch;
            PumpUntil(() => vm.Songs.Count == expected);

            window.Width = page.MinWidth;
            view.UpdateLayout();
            // A direct panel offset models an accessibility scroll request; allow its batch to finish.
            panel.SetVerticalOffset(panel.ExtentHeight - panel.ViewportHeight);
            view.UpdateLayout();
            window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
            var countBeforeReflow = vm.Songs.Count;
            window.Width = 1920;
            view.UpdateLayout();
            window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
            Assert.That(vm.Songs, Has.Count.EqualTo(countBeforeReflow), "Offset clamping during reflow is not a scroll request.");
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
        using var view = new InMemoryView(window);
        try
        {
            page.UpdateSongsLayout(new Size(1920, 1040));
            UiTestPump.Run(page.LoadAsync(CancellationToken.None));

            PumpUntil(() => !vm.IsAwaitingSongsPresentation);
            foreach (var path in validPaths) File.Delete(path);
            const int decodeWidth = 336;
            var cachedLoads = Task.WhenAll(validPaths.Select(path =>
                page.ImageLoader.LoadAsync(path, decodeWidth, CancellationToken.None)));
            UiTestPump.Run(cachedLoads);
            var missingLoad = page.ImageLoader.LoadAsync(
                Path.Combine(imageDirectory.FullName, "missing.png"), decodeWidth, CancellationToken.None);
            UiTestPump.Run(missingLoad);
            PumpUntil(() => !vm.IsAwaitingSongsPresentation);

            Assert.Multiple(() =>
            {
                Assert.That(vm.Songs, Has.Count.EqualTo(HomePageVM.InitialSongCount));
                Assert.That(cachedLoads.Result, Has.All.Not.Null);
                Assert.That(missingLoad.Result, Is.Null);
                Assert.That(vm.IsAwaitingSongsPresentation, Is.False);
            });
        }
        finally
        {
            window.Close();
            imageDirectory.Delete(true);
        }
    }

    [Test]
    public void RetryWaitsForCurrentReplacementPresentation()
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
        using var view = new InMemoryView(window);
        page.UpdateSongsLayout(new Size(1920, 1040));
        try
        {
            UiTestPump.Run(page.LoadAsync(CancellationToken.None));

            PumpUntil(() => !vm.IsAwaitingSongsPresentation);
            vm.ReportDemandFailure(new IOException("retry"));
            folders.Songs = (_, token) => pending.Task.WaitAsync(token);

            var retry = vm.RetrySongsCommand.ExecuteAsync(null);
            PumpUntil(() => vm.IsProcessing);
            Assert.That(vm.IsProcessing, Is.True);
            pending.SetResult(Snapshot("Replacement"));
            UiTestPump.Run(retry);
            PumpUntil(() => !vm.IsAwaitingSongsPresentation);

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
        using var view = new InMemoryView(window);
        page.UpdateSongsLayout(new Size(1920, 1040));
        try
        {
            UiTestPump.Run(page.LoadAsync(CancellationToken.None));

            PumpUntil(() => !vm.IsAwaitingSongsPresentation);
            var initialSongs = vm.Songs.ToArray();
            vm.ReportDemandFailure(new IOException("retry"));
            folders.Songs = (_, token) => pending.Task.WaitAsync(token);
            var retry = vm.RetrySongsCommand.ExecuteAsync(null);
            PumpUntil(() => vm.IsProcessing);

            page.Deactivate();
            pending.TrySetResult(new FolderSnapshot([new FolderEntry(@"C:\Songs\late", "late", DateTimeOffset.UnixEpoch)]));
            UiTestPump.Run(retry);
            window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);

            Assert.That(vm.CanInteract, Is.False);
            Assert.That(vm.Songs, Is.EqualTo(initialSongs), "Deactivation must reject the late replacement.");
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
            new ImmediateUiScheduler(),
            new StubProjectFileService((_, _, _) => Task.FromResult("saved.json")), new WeakReferenceMessenger());
}
