using System.Windows;
using System.Windows.Controls;
using CommunityToolkit.Mvvm.Messaging;
using Hitbench.Resources.CustomControls;
using Hitbench.Resources.DataModels;
using Hitbench.ViewModels.Shell.HomePage;
using Hitbench.Views.Pages;
namespace Hitbench.Test.Presentation;
[Apartment(ApartmentState.STA), NonParallelizable]
public class HomeMapsetsDirectoryIntegrationTests
{
    [TestCase(false, false)] [TestCase(true, false)] [TestCase(true, true)]
    public void LocationActionsFollowInventory(bool resolved, bool populated)
    {
        new SharedUiResourceIntegrationTests().SharedResources_LoadTogetherAndProvideReusableControlContracts();
        var vm = new HomePageVM(new StubHomeFolderService { Songs = (_, _) => Task.FromResult<FolderSnapshot?>(new FolderSnapshot(populated ? [new FolderEntry(@"C:\maps\one", "one", default)] : [])) },
            new StubAppSettingsService { Load = _ => Task.FromResult(new Hitbench.Core.Entities.AppSettings { MapsetsDirectory = resolved ? @"C:\maps" : "" }) },
            new StubInstallationDialogService(), new ImmediateUiScheduler(), new StubProjectFileService((_, _, _) => Task.FromResult("saved")), new WeakReferenceMessenger(), picker: new StubSampleFileDialogService((string?)null));
        var page = new HomePage(vm);
        var window = new Window { Content = page, Width = 1280, Height = 720 };
        using var view = new InMemoryView(window);
        try {
            UiTestPump.Run(page.LoadSongsPresentationAsync(default)); view.UpdateLayout();
            var center = (BalloonButton)page.FindName("LocateOsuFolderButton");
            var toolbar = (BalloonButton)page.FindName("LocateMapsetsDirectoryButton");
            UiControlContract.AssertStyle<BalloonButton>(center, "OHsHomeLocateActionStyle");
            UiControlContract.AssertStyle<BalloonButton>(toolbar, "OHsHomeLocateMapsetsActionStyle");
            Assert.That(center.IsVisible, Is.EqualTo(!populated)); Assert.That(toolbar.IsVisible, Is.EqualTo(populated));
            Assert.That(populated ? toolbar.IsEnabled : center.IsEnabled, Is.True, "Locate action must enable after loading finishes.");
            Assert.That(center.ButtonText, Is.EqualTo("Locate osu! folder"));
            Assert.That(((TextBlock)page.FindName("NoMapsetsText")).IsVisible, Is.EqualTo(resolved && !populated));
            Assert.That(toolbar.ToolTip, Is.EqualTo("Locate mapset folder"));
        } finally { page.Deactivate(); window.Close(); }
    }
}
