using Hitbench.Resources.DataModels;
using Hitbench.Services;
using Hitbench.ViewModels.Popups;
namespace Hitbench.Test.Presentation;

public class InstrumentalSingleSelectionTests
{
    [Test]
    public async Task SingleModeReplacesPreviousChoice()
    {
        var browser = new StubSampleBrowserService(); var preview = new StubSamplePreviewService();
        var first = new SampleBrowserEntry(Path.Combine(browser.RootPath, "a.wav"), "a.wav", false);
        var second = new SampleBrowserEntry(Path.Combine(browser.RootPath, "b.wav"), "b.wav", false);
        browser.DefaultDirectory = browser.RootPath; browser.Entries[browser.RootPath] = [first, second];
        var vm = new SampleLoaderPopupVM(browser, preview, new ImmediateUiScheduler(), new PlaybackVolumeSettings());
        vm.ConfigureSelectionMode(SampleSelectionMode.Single);
        try
        {
            await vm.InitializeAsync(default); await vm.SetCheckedAsync(first, true); await vm.SetCheckedAsync(second, true);
            Assert.That(vm.CheckedCount, Is.EqualTo(1)); Assert.That(first.IsChecked, Is.False);
            Assert.That(await vm.ConfirmAsync(), Is.EqualTo(new[] { second.FullPath }));
        }
        finally { await vm.DeactivateAsync(); }
    }
}
