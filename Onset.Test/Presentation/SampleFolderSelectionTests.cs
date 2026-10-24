using Onset.Resources.DataModels;
using Onset.Services;
using Onset.ViewModels.Popups;

namespace Onset.Test.Presentation;

public sealed class SampleFolderSelectionTests
{
    [TestCase(false)][TestCase(true)]
    public async Task ConfirmsCurrentOrSelectedFolderWithoutAudio(bool selectChild)
    {
        var browser = new StubSampleBrowserService();
        var folder = new SampleBrowserEntry(Path.Combine(browser.RootPath, "Piano"), "Piano", true);
        var audio = new SampleBrowserEntry(Path.Combine(browser.RootPath, "C3.wav"), "C3.wav", false);
        browser.Entries[browser.RootPath] = [folder, audio];
        browser.Metadata = (_, _) => throw new AssertionException("Folder selection must not decode audio.");
        var preview = new StubSamplePreviewService();
        var vm = new SampleLoaderPopupVM(browser, preview, new ImmediateUiScheduler(), new PlaybackVolumeSettings());
        vm.ConfigureSelectionMode(SampleSelectionMode.Folder);
        try
        {
            await vm.InitializeAsync(browser.RootPath, default);
            Assert.That(vm.Entries, Is.EqualTo(new[] { folder }));
            await vm.SelectAsync(audio);
            if (selectChild) await vm.SelectAsync(folder);
            Assert.That(vm.CanPreview, Is.False); Assert.That(vm.CanConfirm, Is.True);
            Assert.That(await vm.ConfirmAsync(), Is.EqualTo(new[] { selectChild ? folder.FullPath : browser.RootPath }));
            Assert.That(preview.PlayedPaths, Is.Empty);
        }
        finally { await vm.DeactivateAsync(); }
    }

    [Test] public async Task ThisPcAndFailedNavigationCannotBeConfirmed()
    {
        var browser = new StubSampleBrowserService(); browser.Entries[browser.RootPath] = [];
        var vm = new SampleLoaderPopupVM(browser, new StubSamplePreviewService(), new ImmediateUiScheduler(), new PlaybackVolumeSettings());
        vm.ConfigureSelectionMode(SampleSelectionMode.Folder);
        try
        {
            await vm.InitializeAsync(browser.RootPath, default);
            await vm.NavigateCommand.ExecuteAsync(null); Assert.That(vm.CanConfirm, Is.False);
            Assert.That(await vm.ConfirmAsync(), Is.Empty);
            await vm.NavigateCommand.ExecuteAsync(Path.Combine(browser.RootPath, "missing"));
            Assert.That(vm.CanConfirm, Is.False);
        }
        finally { await vm.DeactivateAsync(); }
    }
}
