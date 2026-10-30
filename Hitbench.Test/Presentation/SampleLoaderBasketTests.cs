using Hitbench.Resources.DataModels;
using Hitbench.Services;
using Hitbench.ViewModels.Popups;

namespace Hitbench.Test.Presentation;

public sealed class SampleLoaderBasketTests
{
    [TestCase(false)]
    [TestCase(true)]
    public async Task SummaryCountsSelectionsAcrossFoldersWithoutMetadata(bool metadataFails)
    {
        var browser = new StubSampleBrowserService { DefaultDirectory = "" };
        browser.DefaultDirectory = browser.RootPath;
        var first = new SampleBrowserEntry(Path.Combine(browser.RootPath, "one.wav"), "one.wav", false);
        var other = Path.Combine(browser.RootPath, "Other");
        var second = new SampleBrowserEntry(Path.Combine(other, "two.wav"), "two.wav", false);
        browser.Entries[browser.RootPath] = [first]; browser.Entries[other] = [second];
        var pending = new TaskCompletionSource<SampleFileMetadata>();
        browser.Metadata = (_, token) => metadataFails
            ? Task.FromException<SampleFileMetadata>(new IOException("Unavailable")) : pending.Task.WaitAsync(token);
        var vm = new SampleLoaderPopupVM(browser, new StubSamplePreviewService(), new ImmediateUiScheduler(), new PlaybackVolumeSettings()) { AutoPlaySamples = false };
        try
        {
            await vm.InitializeAsync(default(CancellationToken));
            Assert.That(vm.SelectionSummary, Is.EqualTo("0 samples"));
            await vm.SetCheckedAsync(first, true);
            Assert.That(vm.SelectionSummary, Is.EqualTo("1 sample"));
            await vm.SetCheckedAsync(second, true);
            Assert.That(vm.SelectionSummary, Is.EqualTo("2 samples"));
            vm.ClearSelectedCommand.Execute(null);
            Assert.That(vm.SelectionSummary, Is.EqualTo("0 samples"));
        }
        finally { await vm.DeactivateAsync(); }
    }

    [Test]
    public async Task ListingMetadataUsesAtMostTwoReadersAndCancelsOnNavigation()
    {
        var browser = new StubSampleBrowserService();
        var files = Enumerable.Range(0, 5).Select(i => new SampleBrowserEntry(Path.Combine(browser.RootPath, i + ".wav"), i + ".wav", false)).ToArray();
        browser.DefaultDirectory = browser.RootPath; browser.Entries[browser.RootPath] = files;
        var other = Path.Combine(browser.RootPath, "Other"); browser.Entries[other] = [];
        var twoReaders = new TaskCompletionSource();
        var release = new TaskCompletionSource();
        var active = 0; var maximum = 0;
        browser.Metadata = async (path, token) =>
        {
            var count = Interlocked.Increment(ref active);
            int seen;
            do { seen = maximum; } while (count > seen && Interlocked.CompareExchange(ref maximum, count, seen) != seen);
            if (count == 1) twoReaders.TrySetResult();
            try { await release.Task.WaitAsync(token); return new(path, Path.GetFileName(path), "WAV", 1024, TimeSpan.FromSeconds(1), 44100, 1); }
            finally { Interlocked.Decrement(ref active); }
        };
        var vm = new SampleLoaderPopupVM(browser, new StubSamplePreviewService(), new ImmediateUiScheduler(), new PlaybackVolumeSettings()) { AutoPlaySamples = false };
        try
        {
            await vm.InitializeAsync(default(CancellationToken));
            await twoReaders.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.That(maximum, Is.EqualTo(1));
            await vm.NavigateCommand.ExecuteAsync(other);
            await vm.DeactivateAsync();
            Assert.That(active, Is.Zero);
            Assert.That(files.All(x => x.Metadata is null), Is.True);
        }
        finally { release.TrySetResult(); await vm.DeactivateAsync(); }
    }
    [Test]
    public async Task UnavailableMetadataDoesNotPresentACachedDurationAsComplete()
    {
        var browser = new StubSampleBrowserService();
        var file = new SampleBrowserEntry(Path.Combine(browser.RootPath, "hit.wav"), "hit.wav", false);
        browser.DefaultDirectory = browser.RootPath; browser.Entries[browser.RootPath] = [file];
        var vm = new SampleLoaderPopupVM(browser, new StubSamplePreviewService(), new ImmediateUiScheduler(), new PlaybackVolumeSettings()) { AutoPlaySamples = false };
        try
        {
            await vm.InitializeAsync(default(CancellationToken)); await vm.SetCheckedAsync(file, true);
            var unavailable = new TaskCompletionSource();
            file.PropertyChanged += (_, args) => { if (args.PropertyName == nameof(file.IsMetadataUnavailable) && file.IsMetadataUnavailable) unavailable.TrySetResult(); };
            var otherFolder = Path.Combine(browser.RootPath, "Other");
            browser.Entries[otherFolder] = [];
            browser.Metadata = (_, _) => Task.FromException<SampleFileMetadata>(new IOException("File became unreadable"));
            await vm.NavigateCommand.ExecuteAsync(otherFolder);
            await vm.NavigateCommand.ExecuteAsync(browser.RootPath);
            await unavailable.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.That(vm.SelectedDurationText, Is.EqualTo("—"));
            Assert.That(vm.CanConfirm, Is.True);
        }
        finally { await vm.DeactivateAsync(); }
    }
    [TestCase(SampleSelectionMode.Multiple, 2)]
    [TestCase(SampleSelectionMode.Single, 1)]
    public async Task SelectionModeAppliesAcrossDirectories(SampleSelectionMode mode, int count)
    {
        var browser = new StubSampleBrowserService();
        var a = new SampleBrowserEntry(Path.Combine(browser.RootPath, "A", "same.wav"), "same.wav", false);
        var b = new SampleBrowserEntry(Path.Combine(browser.RootPath, "B", "same.wav"), "same.wav", false);
        browser.Entries[Path.GetDirectoryName(a.FullPath)!] = [a]; browser.Entries[Path.GetDirectoryName(b.FullPath)!] = [b];
        var vm = new SampleLoaderPopupVM(browser, new StubSamplePreviewService(), new ImmediateUiScheduler(), new PlaybackVolumeSettings()) { AutoPlaySamples = false };
        try
        {
            await vm.InitializeAsync(new SamplePickerContext(null, Path.GetDirectoryName(a.FullPath), mode), default);
            await vm.SetCheckedAsync(a, true); await vm.SetCheckedAsync(b, true);
            Assert.That(vm.CheckedCount, Is.EqualTo(count));
            Assert.That(a.IsChecked, Is.EqualTo(mode == SampleSelectionMode.Multiple));
            Assert.That((await vm.ConfirmAsync()).Last(), Is.EqualTo(b.FullPath));
        }
        finally { await vm.DeactivateAsync(); }
    }

    [Test]
    public async Task EquivalentAbsolutePathsRestoreOneCanonicalSelection()
    {
        var browser = new StubSampleBrowserService();
        var folder = Path.Combine(browser.RootPath, "A");
        var file = new SampleBrowserEntry(Path.Combine(folder, "hit.wav"), "hit.wav", false);
        browser.Entries[folder] = [file]; browser.Entries[browser.RootPath] = [];
        var vm = new SampleLoaderPopupVM(browser, new StubSamplePreviewService(), new ImmediateUiScheduler(), new PlaybackVolumeSettings()) { AutoPlaySamples = false };
        try
        {
            await vm.InitializeAsync(folder, default);
            await vm.SetCheckedAsync(file, true);
            await vm.NavigateCommand.ExecuteAsync(browser.RootPath);
            browser.Entries[folder] = [new(Path.Combine(folder, ".", "HIT.WAV"), "HIT.WAV", false)];
            await vm.NavigateCommand.ExecuteAsync(folder);
            Assert.That(vm.Entries.Single().IsChecked, Is.True);
            Assert.That(vm.CheckedCount, Is.EqualTo(1));
            Assert.That(await vm.ConfirmAsync(), Is.EqualTo(new[] { file.FullPath }));
        }
        finally { await vm.DeactivateAsync(); }
    }
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
            vm.SearchText = "no match"; await vm.SearchCompletion;
            Assert.That(vm.VisibleEntries, Is.Empty);
            Assert.That(vm.CheckedCount, Is.EqualTo(2));
            vm.SearchText = "SAME"; await vm.SearchCompletion;
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
