using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using osuHitsounder.Resources.CustomControls;
using osuHitsounder.Resources.DataModels;
using osuHitsounder.Services;
using osuHitsounder.ViewModels.Popups;
using osuHitsounder.Views.Popups;

namespace osuHitsounder.Test.Presentation;

[Apartment(ApartmentState.STA), NonParallelizable]
public sealed class SampleFolderSelectionIntegrationTests
{
    [Test] public void FolderModeUsesExistingActionContractAndReturnsFolder()
    {
        new SharedUiResourceIntegrationTests().SharedResources_LoadTogetherAndProvideReusableControlContracts();
        var browser = new StubSampleBrowserService(); browser.Entries[browser.RootPath] = [];
        var vm = new SampleLoaderPopupVM(browser, new StubSamplePreviewService(), new ImmediateUiScheduler(), new PlaybackVolumeSettings());
        vm.ConfigureSelectionMode(SampleSelectionMode.Folder);
        bool? completion = null;
        var popup = new SampleLoaderPopup(vm, result => completion = result, _ => new Size(640, 400));
        UiTestPump.Run(popup.InitializeAsync(browser.RootPath, default));
        using var view = new InMemoryView(popup);
        try
        {
            var confirm = (BalloonButton)popup.FindName("ImportButton");
            Assert.That(confirm.Style, Is.SameAs(popup.FindResource("SampleLoaderImportStyle")));
            Assert.That(confirm.ButtonText, Is.EqualTo("Select folder"));
            Assert.That(confirm.IsEnabled, Is.True); Assert.That(confirm.IsTabStop, Is.True);
            Assert.That(((CheckBox)popup.FindName("AutoPlayCheckBox")).Visibility, Is.EqualTo(Visibility.Collapsed));
            Assert.That(((Grid)popup.FindName("SelectedFileDetails")).Visibility, Is.EqualTo(Visibility.Collapsed));
            confirm.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            Assert.That(completion, Is.True); Assert.That(popup.Result, Is.EqualTo(new[] { browser.RootPath }));
        }
        finally { UiTestPump.Run(popup.DeactivateAsync()); popup.Close(); }
    }
}
