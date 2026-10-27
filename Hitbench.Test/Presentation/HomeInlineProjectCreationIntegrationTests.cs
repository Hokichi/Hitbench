using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Messaging;
using Hitbench.Resources.CustomControls;
using Hitbench.Resources.DataModels;
using Hitbench.ViewModels.Shell.HomePage;
using Hitbench.Views.Pages;

namespace Hitbench.Test.Presentation;

[Apartment(ApartmentState.STA), NonParallelizable]
public sealed class HomeInlineProjectCreationIntegrationTests
{
    [Test]
    public void SwitchingMapsetsCancelsHeaderDraft()
    {
        new SongsResponsiveLayoutTests().SetUp();
        var first = HomeInlineProjectCreationTests.Mapset;
        var second = first with { FullName = @"C:\Maps\second" };
        string? savedFor = null;
        var vm = new HomePageVM(new StubHomeFolderService
        {
            Songs = (_, _) => Task.FromResult<FolderSnapshot?>(new FolderSnapshot([first, second])),
            Projects = _ => Task.FromResult(FolderSnapshot.ForProjects([], true))
        }, new StubAppSettingsService(), new StubInstallationDialogService(), new ImmediateUiScheduler(),
            new StubProjectFileService((folder, _, _) => { savedFor = folder.FullName; return Task.FromResult("saved.json"); }), new WeakReferenceMessenger());
        var page = new HomePage(vm);
        var window = new Window { Content = page, Width = 1280, Height = 720 };
        using var view = new InMemoryView(window);
        try
        {
            UiTestPump.Run(page.LoadAsync(default));
            vm.SelectMapsetCommand.Execute(first); UiTestPump.Run(vm.SearchProjectsCommand.ExecutionTask!);
            UiTestPump.Run(vm.ShowProjectCreationCommand.ExecuteAsync(null)); view.UpdateLayout();
            var input = (TextBox)page.FindName("ProjectNameTextBox"); input.Text = "old draft";
            vm.SelectMapsetCommand.Execute(second); UiTestPump.Run(vm.SearchProjectsCommand.ExecutionTask!); view.UpdateLayout();
            Assert.That(input.IsVisible, Is.False); Assert.That(vm.DraftProjectName, Is.Empty);
            UiTestPump.Run(vm.ShowProjectCreationCommand.ExecuteAsync(null)); view.UpdateLayout(); input.Text = "second project";
            Assert.That(page.HandleProjectNameKey(Key.Enter), Is.True); UiTestPump.Run(vm.CreateProjectCommand.ExecutionTask!);
            Assert.That(savedFor, Is.EqualTo(second.FullName));
        }
        finally { page.Deactivate(); window.Close(); }
    }

    [Test]
    public void SelectionDisablesSearchAndInlineEditorUsesValidatedCommand()
    {
        new SongsResponsiveLayoutTests().SetUp();
        var mapset = HomeInlineProjectCreationTests.Mapset;
        var saves = 0;
        var vm = new HomePageVM(new StubHomeFolderService
        {
            Songs = (_, _) => Task.FromResult<FolderSnapshot?>(new FolderSnapshot([mapset])),
            Projects = _ => Task.FromResult(FolderSnapshot.ForProjects([], true))
        }, new StubAppSettingsService(), new StubInstallationDialogService(), new ImmediateUiScheduler(),
            new StubProjectFileService((_, _, _) => { saves++; return Task.FromResult("saved.json"); }), new WeakReferenceMessenger());
        var page = new HomePage(vm);
        var window = new Window { Content = page, Width = 1280, Height = 720 };
        using var view = new InMemoryView(window);
        try
        {
            UiTestPump.Run(page.LoadAsync(default)); view.UpdateLayout();
            Assert.That(page.FindName("SongsList"), Is.TypeOf<ListBox>());
            var search = (TextBox)page.FindName("ProjectSearchTextBox");
            search.Text = "retained query";
            UiTestPump.Run(vm.SearchProjectsCommand.ExecutionTask!);
            vm.SelectMapsetCommand.Execute(mapset);
            UiTestPump.Run(vm.SearchProjectsCommand.ExecutionTask!); view.UpdateLayout();
            Assert.That(((TextBox)page.FindName("ProjectSearchTextBox")).IsEnabled, Is.False);
            Assert.That(search.IsVisible, Is.False);
            var show = (BalloonButton)page.FindName("ShowProjectCreationButton");
            Assert.That(show.IsVisible, Is.True);
            Assert.That(show.Command, Is.SameAs(vm.ShowProjectCreationCommand));
            UiTestPump.Run(vm.ShowProjectCreationCommand.ExecuteAsync(null)); view.UpdateLayout();
            var input = (TextBox)page.FindName("ProjectNameTextBox");
            var create = (BalloonButton)page.FindName("CreateProjectButton");
            UiControlContract.AssertStyle<TextBox>(input, "OHsHomeProjectNameInputStyle");
            Assert.That(input.IsVisible, Is.True);
            Assert.That(create.Command, Is.SameAs(vm.CreateProjectCommand));
            Assert.That(create.IsEnabled, Is.False);
            input.Text = "CON"; view.UpdateLayout();
            Assert.That(Validation.GetHasError(input), Is.True);
            Assert.That(create.IsEnabled, Is.False);
            input.Text = "New project"; view.UpdateLayout();
            Assert.That(vm.DraftProjectName, Is.EqualTo("New project"));
            Assert.That(create.IsEnabled, Is.True);
            Assert.That(page.HandleProjectNameKey(Key.Escape), Is.True); view.UpdateLayout();
            Assert.That(input.IsVisible, Is.False);
            Assert.That(saves, Is.Zero);
            UiTestPump.Run(vm.ShowProjectCreationCommand.ExecuteAsync(null)); view.UpdateLayout();
            input.Text = "New project"; view.UpdateLayout();
            Assert.That(page.HandleProjectNameKey(Key.Enter), Is.True);
            UiTestPump.Run(vm.CreateProjectCommand.ExecutionTask!);
            Assert.That(saves, Is.EqualTo(1));
            vm.SelectMapsetCommand.Execute(mapset);
            UiTestPump.Run(vm.SearchProjectsCommand.ExecutionTask!); view.UpdateLayout();
            Assert.That(((TextBox)page.FindName("ProjectSearchTextBox")).IsEnabled, Is.True);
            Assert.That(search.IsVisible, Is.True);
            Assert.That(search.Text, Is.EqualTo("retained query"));
            Assert.That(show.IsVisible, Is.False);
        }
        finally { page.Deactivate(); window.Close(); }
    }
}
