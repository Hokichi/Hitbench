using Hitbench.Resources.DataModels;
using Hitbench.Services;
using Hitbench.ViewModels.Popups;
namespace Hitbench.Test.Presentation;
public sealed class SampleSearchDebounceTests
{
    [Test]
    public async Task FilterWaitsForDelayAndClearIsImmediate()
    {
        var browser = new StubSampleBrowserService(); browser.DefaultDirectory = browser.RootPath;
        browser.Entries[browser.RootPath] = [new(Path.Combine(browser.RootPath,"one.wav"),"one.wav",false), new(Path.Combine(browser.RootPath,"two.wav"),"two.wav",false)];
        var delay = new TaskCompletionSource();
        var vm = new SampleLoaderPopupVM(browser, new StubSamplePreviewService(), new ImmediateUiScheduler(), new PlaybackVolumeSettings(), searchDelay: (_, token) => delay.Task.WaitAsync(token));
        try
        {
            await vm.InitializeAsync(default(CancellationToken));
            vm.SearchText = "one";
            Assert.That(vm.VisibleEntries, Has.Count.EqualTo(2));
            delay.SetResult(); await vm.SearchCompletion;
            Assert.That(vm.VisibleEntries.Single().Name, Is.EqualTo("one.wav"));
            vm.SearchText = ""; await vm.SearchCompletion;
            Assert.That(vm.VisibleEntries, Has.Count.EqualTo(2));
        }
        finally { await vm.DeactivateAsync(); }
    }
}
