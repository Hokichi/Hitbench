using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using CommunityToolkit.Mvvm.Messaging;
using Hitbench.Resources.CustomControls;
using Hitbench.Resources.DataModels;
using Hitbench.ViewModels.Shell.HomePage;
using Hitbench.Views.Pages;

namespace Hitbench.Test.Presentation;

[Apartment(ApartmentState.STA), NonParallelizable]
public sealed class HomePageCardIntegrationTests
{
    private HomePageVM vm = null!;
    private HomePage page = null!;
    private InMemoryView view = null!;
    private Window window = null!;

    [SetUp]
    public void SetUp()
    {
        new SongsResponsiveLayoutTests().SetUp();
        var folders = new StubHomeFolderService
        {
            Songs = (_, _) => Task.FromResult<FolderSnapshot?>(new FolderSnapshot(Enumerable.Range(0, 500)
                .Select(i => new FolderEntry($@"C:\Songs\{i:D3}", $"Song {i:D3}", default)))),
            Projects = _ => Task.FromResult(FolderSnapshot.ForProjects(Enumerable.Range(0, 200)
                .Select(i => new FolderEntry($@"C:\Projects\{i:D3}", $"Project {i:D3}", default) { HasProjectMetadata = true }), true))
        };
        vm = new HomePageVM(folders, new StubAppSettingsService(), new StubInstallationDialogService(),
            new ImmediateUiScheduler(),
            new StubProjectFileService((_, _, _) => Task.FromResult("saved.json")), new WeakReferenceMessenger());
        page = new HomePage(vm);
        window = new Window { Content = page, Width = 1920, Height = 1040 };
        view = new InMemoryView(window);
        page.UpdateSongsLayout(new Size(1920, 1040));
        UiTestPump.Run(page.LoadSongsPresentationAsync(default));
        view.UpdateLayout();
    }

    [TearDown]
    public void TearDown() { page.Deactivate(); view.Dispose(); window.Close(); }

    private FrameworkElement Card(bool project)
    {
        var list = (ItemsControl)page.FindName(project ? "ProjectsList" : "SongsList");
        var style = page.FindResource(project ? "OHsHomePageProjectCardBorderStyle" : "HomePageSongCardButtonStyle");
        return Descendants<FrameworkElement>(list).First(element => ReferenceEquals(element.Style, style));
    }

    [TestCase("Dark", FlowDirection.LeftToRight), TestCase("Light", FlowDirection.RightToLeft)]
    public void BothCardFamiliesRetainControlAndCommandContracts(string theme, FlowDirection direction)
    {
        Application.Current.Resources.MergedDictionaries[0] = new ResourceDictionary
            { Source = new Uri($"/Hitbench;component/Resources/Themes/{theme}.xaml", UriKind.Relative) };
        window.FlowDirection = direction;
        view.UpdateLayout();
        var song = (Button)Card(false);
        var project = (Border)Card(true);
        var songContext = song.DataContext;
        var projectContext = project.DataContext;
        UiControlContract.AssertStyle<Button>(song, "HomePageSongCardButtonStyle");
        UiControlContract.AssertTemplate(song, "FolderActionButtonTemplate");
        UiControlContract.AssertStyle<Border>(project, "OHsHomePageProjectCardBorderStyle");
        Assert.That(song.DataContext, Is.SameAs(songContext));
        Assert.That(project.DataContext, Is.SameAs(projectContext));
        Assert.That(song.Command, Is.SameAs(vm.SelectMapsetCommand));
        Assert.That(Descendants<Button>(project).Single().Command, Is.SameAs(vm.OpenProjectCommand));
    }

    [Test]
    public void ReloadGatesMapsetActionsAndPreservesSelection()
    {
        var song = (Button)Card(false);
        var selected = (FolderEntry)song.DataContext;
        vm.SelectMapsetCommand.Execute(selected);
        if (vm.SearchProjectsCommand.ExecutionTask is { } filtering) UiTestPump.Run(filtering);
        var ready = new TaskCompletionSource();
        vm.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(vm.CanSelectMapset) && vm.CanSelectMapset) ready.TrySetResult(); };
        var reload = vm.ReloadSongsCommand.ExecuteAsync(null);
        Assert.That(((ListBox)page.FindName("SongsList")).IsEnabled, Is.False);
        UiTestPump.Run(reload);
        UiTestPump.Run(ready.Task);
        view.UpdateLayout();
        Assert.That(vm.SelectedMapset?.FullName, Is.EqualTo(selected.FullName));
        Assert.That(vm.CanSelectMapset, Is.True);
    }

    [Test]
    public void DeactivateDisablesCardCommands()
    {
        var song = (Button)Card(false);
        var selected = (FolderEntry)song.DataContext;
        page.Deactivate();
        Assert.That(vm.SelectMapsetCommand.CanExecute(selected), Is.False);
        Assert.That(vm.OpenProjectCommand.CanExecute(new FolderEntry("project", "project", default)), Is.False);
    }

    [TestCase(false), TestCase(true)]
    public void RecycledCardsKeepCurrentCommandContext(bool project)
    {
        UiTestPump.Run(vm.LoadMoreSongsCommand.ExecuteAsync(300));
        view.UpdateLayout();
        var list = (ItemsControl)page.FindName(project ? "ProjectsList" : "SongsList");
        Descendants<ScrollViewer>(list).First().ScrollToEnd();
        view.UpdateLayout();
        var card = Card(project);
        var button = project ? Descendants<Button>(card).Single() : (Button)card;
        Assert.That(button.Command, Is.SameAs(project ? vm.OpenProjectCommand : vm.SelectMapsetCommand));
        var entry = (FolderEntry)card.DataContext;
        Assert.That(button.CommandParameter, Is.SameAs(entry));
    }

    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T value) yield return value;
            foreach (var descendant in Descendants<T>(child)) yield return descendant;
        }
    }
}
