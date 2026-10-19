using osuHitsounder.Resources.DataModels;
using osuHitsounder.Services;
using osuHitsounder.ViewModels.Popups;

namespace osuHitsounder.Test.Presentation;

public sealed class SampleLoaderBasketTests
{
    [Test]
    public async Task ListingMetadataPopulatesRowsWithoutBlockingInitialization()
    {
        var browser = new StubSampleBrowserService();
        var completion = new TaskCompletionSource<SampleFileMetadata>();
        var file = new SampleBrowserEntry(Path.Combine(browser.RootPath, "hit.wav"), "hit.wav", false);
        browser.DefaultDirectory = browser.RootPath; browser.Entries[browser.RootPath] = [file];
        browser.Metadata = (_, _) => completion.Task;
        var vm = new SampleLoaderPopupVM(browser, new StubSamplePreviewService(), new ImmediateUiScheduler(), new PlaybackVolumeSettings());
        try
        {
            await vm.InitializeAsync(default(CancellationToken));
            Assert.That(file.IsMetadataLoading, Is.True);
            completion.SetResult(new(file.FullPath, file.Name, "WAV", 1024, TimeSpan.FromSeconds(.31), 44100, 1));
            for (var i = 0; i < 100 && file.Metadata is null; i++) await Task.Delay(5);
            Assert.That(file.Metadata?.Duration, Is.EqualTo(TimeSpan.FromSeconds(.31)));
        }
        finally { completion.TrySetCanceled(); await vm.DeactivateAsync(); }
    }
    [Test]
    public async Task CrossFolderSelectionSurvivesSearchAndReturnsDistinctPaths()
    {
        var browser = new StubSampleBrowserService();
        var folderA = Path.Combine(browser.RootPath, "A");
        var folderB = Path.Combine(browser.RootPath, "B");
        var first = new SampleBrowserEntry(Path.Combine(folderA, "same.wav"), "same.wav", false);
        var second = new SampleBrowserEntry(Path.Combine(folderB, "same.wav"), "same.wav", false);
        browser.DefaultDirectory = folderA;
        browser.Entries[folderA] = [first]; browser.Entries[folderB] = [second];
        var vm = new SampleLoaderPopupVM(browser, new StubSamplePreviewService(), new ImmediateUiScheduler(), new PlaybackVolumeSettings()) { AutoPlaySamples = false };
        try
        {
            await vm.InitializeAsync(default(CancellationToken));
            await vm.SetCheckedAsync(first, true);
            await vm.NavigateCommand.ExecuteAsync(folderB);
            await vm.SetCheckedAsync(second, true);
            vm.SearchText = "no match";
            Assert.That(vm.VisibleEntries, Is.Empty);
            Assert.That(vm.CheckedCount, Is.EqualTo(2));
            vm.SearchText = "SAME";
            Assert.That(vm.VisibleEntries.Single(), Is.SameAs(second));
            await vm.NavigateCommand.ExecuteAsync(folderA);
            Assert.That(first.IsChecked, Is.True);
            Assert.That(await vm.ConfirmAsync(), Is.EqualTo(new[] { first.FullPath, second.FullPath }));
        }
        finally { await vm.DeactivateAsync(); }
    }

    [Test]
    public async Task RangeAndClearSynchronizeVisibleChecks()
    {
        var browser = new StubSampleBrowserService();
        var entries = Enumerable.Range(1, 3).Select(i => new SampleBrowserEntry(Path.Combine(browser.RootPath, i + ".wav"), i + ".wav", false)).ToArray();
        browser.DefaultDirectory = browser.RootPath; browser.Entries[browser.RootPath] = entries;
        var vm = new SampleLoaderPopupVM(browser, new StubSamplePreviewService(), new ImmediateUiScheduler(), new PlaybackVolumeSettings()) { AutoPlaySamples = false };
        try
        {
            await vm.InitializeAsync(default(CancellationToken));
            vm.SetRangeAnchor(entries[0]);
            await vm.SetRangeCheckedAsync(entries[2], true);
            Assert.That(vm.CheckedCount, Is.EqualTo(3));
            vm.RemoveSelectedCommand.Execute(entries[1]);
            Assert.That(entries[1].IsChecked, Is.False);
            vm.ClearSelectedCommand.Execute(null);
            Assert.That(vm.SelectedSamples, Is.Empty);
            Assert.That(entries.All(e => !e.IsChecked), Is.True);
        }
        finally { await vm.DeactivateAsync(); }
    }
}
