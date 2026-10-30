using Hitbench.Resources.DataModels;
using Hitbench.Services;
using Hitbench.ViewModels.Popups;

namespace Hitbench.Test.Presentation;

public sealed class SampleFolderSelectionTests
{
    [TestCase(OHsFolderSelectionPurpose.OsuInstallation)]
    [TestCase(OHsFolderSelectionPurpose.Mapsets)]
    public async Task MissingHomeInitialDirectoryOpensExistingParent(OHsFolderSelectionPurpose purpose)
    {
        var root = Directory.CreateTempSubdirectory("home-picker-");
        using var browser = new Hitbench.Services.FileSystem.SampleBrowserService(applicationDirectory: root.FullName);
        var vm = new SampleLoaderPopupVM(browser, new StubSamplePreviewService(), new ImmediateUiScheduler(), new PlaybackVolumeSettings());
        try
        {
            var missing = Path.Combine(root.FullName, "removed", "Songs");
            await vm.InitializeAsync(new SamplePickerContext(null, missing, SampleSelectionMode.Folder, missing, purpose), default);
            Assert.That(vm.CurrentDirectory, Is.EqualTo(root.FullName));
            Assert.That(vm.DirectoryError, Is.Null);
            Assert.That(vm.CanConfirm, Is.True);
            Assert.That(vm.Locations[0].IsAvailable, Is.False, "Missing source shortcut must not navigate to the missing folder.");
            Assert.That(Directory.Exists(missing), Is.False, "Opening a picker must not create the missing Songs folder.");
        }
        finally { await vm.DeactivateAsync(); root.Delete(true); }
    }

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
