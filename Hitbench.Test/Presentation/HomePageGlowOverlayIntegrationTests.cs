using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Effects;
using CommunityToolkit.Mvvm.Messaging;
using Hitbench.Resources.CustomControls;
using Hitbench.Resources.DataModels;
using Hitbench.Resources.Helpers;
using Hitbench.ViewModels.Shell.HomePage;
using Hitbench.Views.Pages;

namespace Hitbench.Test.Presentation;

[Apartment(ApartmentState.STA), NonParallelizable]
public sealed class HomePageGlowOverlayIntegrationTests
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
        UiTestPump.Run(page.LoadAsync(default));
        view.UpdateLayout();
    }

    [TearDown]
    public void TearDown() { page.Deactivate(); view.Dispose(); window.Close(); }

    private AdornerDecorator Host
    {
        get
        {
            var host = page.FindName("CardGlowHost") as AdornerDecorator;
            Assert.That(host, Is.Not.Null, "HomePage must provide an explicit outer glow host.");
            return host!;
        }
    }

    private FrameworkElement Card(bool project)
    {
        var list = (ItemsControl)page.FindName(project ? "ProjectsList" : "SongsList");
        var style = page.FindResource(project ? "OHsHomePageProjectCardBorderStyle" : "HomePageSongCardButtonStyle");
        return Descendants<FrameworkElement>(list).First(element => ReferenceEquals(element.Style, style));
    }

    private void Activate(FrameworkElement card) => OuterGlow.SetEffect(card, (DropShadowEffect)card.FindResource("OHsHomePageSongCardGlowEffect"));

    [TestCase("Dark", FlowDirection.LeftToRight), TestCase("Light", FlowDirection.RightToLeft)]
    public void BothCardFamiliesUseOuterHostAndRetainControlContracts(string theme, FlowDirection direction)
    {
        Application.Current.Resources.MergedDictionaries[0] = new ResourceDictionary
            { Source = new Uri($"/Hitbench;component/Resources/Themes/{theme}.xaml", UriKind.Relative) };
        window.FlowDirection = direction;
        view.UpdateLayout();
        Assert.That(OuterGlow.GetIsHost(Host), Is.True);
        var song = (Button)Card(false);
        var project = (Border)Card(true);
        var songContext = song.DataContext;
        var projectContext = project.DataContext;
        Activate(song);
        Activate(project);
        view.UpdateLayout();
        foreach (var card in new FrameworkElement[] { song, project })
        {
            Assert.That(Host.AdornerLayer.GetAdorners(card), Has.Length.EqualTo(1));
            Assert.That(AdornerLayer.GetAdornerLayer(card).GetAdorners(card), Is.Null);
            Assert.That(card.Effect, Is.Null);
        }
        UiControlContract.AssertStyle<Button>(song, "HomePageSongCardButtonStyle");
        UiControlContract.AssertTemplate(song, "FolderActionButtonTemplate");
        UiControlContract.AssertStyle<Border>(project, "OHsHomePageProjectCardBorderStyle");
        Assert.That(song.DataContext, Is.SameAs(songContext));
        Assert.That(project.DataContext, Is.SameAs(projectContext));
        Assert.That(song.Command, Is.SameAs(vm.CreateProjectCommand));
        Assert.That(Descendants<Button>(project).Single().Command, Is.SameAs(vm.OpenProjectCommand));
    }

    [Test]
    public void DeactivateReleasesAllCardGlows()
    {
        var song = Card(false);
        var project = Card(true);
        Activate(song);
        Activate(project);
        Assert.That(Host.AdornerLayer.GetAdorners(song), Has.Length.EqualTo(1));
        Assert.That(Host.AdornerLayer.GetAdorners(project), Has.Length.EqualTo(1));
        page.Deactivate();
        view.UpdateLayout();
        Assert.That(Host.AdornerLayer.GetAdorners(song), Is.Null);
        Assert.That(Host.AdornerLayer.GetAdorners(project), Is.Null);
    }

    [TestCase(false), TestCase(true)]
    public void RecycledCardsDoNotLeaveOldGlows(bool project)
    {
        UiTestPump.Run(vm.LoadMoreSongsCommand.ExecuteAsync(300));
        view.UpdateLayout();
        var card = Card(project);
        Activate(card);
        var old = Host.AdornerLayer.GetAdorners(card)!.Single();
        var list = (ItemsControl)page.FindName(project ? "ProjectsList" : "SongsList");
        Descendants<ScrollViewer>(list).First().ScrollToEnd();
        view.UpdateLayout();
        var remaining = Descendants<OuterGlowAdorner>(Host.AdornerLayer).ToArray();
        Assert.That(remaining, Does.Not.Contain(old));
        Assert.That(remaining.All(adorner => adorner.AdornedElement.IsDescendantOf(list)), Is.True);
        Assert.That(remaining.Select(adorner => adorner.AdornedElement).Distinct().Count(), Is.EqualTo(remaining.Length));
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
