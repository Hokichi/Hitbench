using osuHitsounder.Resources.DataModels;
using osuHitsounder.Services;
using osuHitsounder.ViewModels.Popups;

namespace osuHitsounder.Test.Presentation;

public sealed class SampleLoaderPopupVMTests
{
    [Test]
    public async Task SavedShortcutUsesProjectDirectoryAndMissingContextDisablesShortcuts()
    {
        var projectPath = Path.Combine(browser.RootPath, "Project", "project.json");
        var savedPath = Path.Combine(browser.RootPath, "Project", "Saved Samples");
        browser.Entries[savedPath] = [];
        var model = new SampleLoaderPopupVM(browser, new StubSamplePreviewService(), new ImmediateUiScheduler(), new PlaybackVolumeSettings());
        try
        {
            await model.InitializeAsync(new SamplePickerContext(projectPath, a.FullPath), default);
            var saved = model.Locations.Single(x => x.Name == "Saved Samples");
            Assert.That(saved.FullPath, Is.EqualTo(savedPath));
            Assert.That(saved.IsAvailable, Is.True);
            await model.NavigateLocationCommand.ExecuteAsync(saved);
            Assert.That(model.CurrentDirectory, Is.EqualTo(savedPath));
            Assert.That(model.ActiveLocation, Is.SameAs(saved));
            Assert.That(vm.Locations.Where(x => x.Section == "This project").All(x => !x.IsAvailable), Is.True);
        }
        finally { await model.DeactivateAsync(); }
    }
    private StubSampleBrowserService browser = null!;
    private StubSamplePreviewService preview = null!;
    private SampleLoaderPopupVM vm = null!;
    private SampleBrowserEntry a = null!, b = null!, first = null!, second = null!;
    [SetUp]
    public async Task SetUp()
    {
        browser = new(); preview = new();
        a = new(Path.Combine(browser.RootPath, "A"), "A", true);
        b = new(Path.Combine(browser.RootPath, "B"), "B", true);
        first = new(Path.Combine(a.FullPath, "first.wav"), "first.wav", false);
        second = new(Path.Combine(b.FullPath, "second.wav"), "second.wav", false);
        browser.Entries[browser.RootPath] = [a, b]; browser.Entries[a.FullPath] = [first]; browser.Entries[b.FullPath] = [second];
        browser.DefaultDirectory = a.FullPath;
        vm = new(browser, preview, new ImmediateUiScheduler(), new PlaybackVolumeSettings());
        await vm.InitializeAsync(CancellationToken.None);
    }
    [TearDown] public Task TearDown() => vm.DeactivateAsync();

    [Test]
    public async Task FailedDriveListingPreservesCheckedFolder()
    {
        await vm.SetCheckedAsync(first, true);
        browser.RootListing = _ => Task.FromException<IReadOnlyList<SampleBrowserEntry>>(new IOException("Drives unavailable"));
        await vm.NavigateCommand.ExecuteAsync(null);
        Assert.That(vm.CurrentDirectory, Is.EqualTo(a.FullPath));
        Assert.That(first.IsChecked, Is.True);
        Assert.That(vm.DirectoryError, Is.EqualTo("Drives unavailable"));
    }

    [Test]
    public async Task EmptyDriveListHasItsOwnStatus()
    {
        browser.RootEntries = [];
        await vm.NavigateCommand.ExecuteAsync(null);
        Assert.That(vm.IsFolderEmpty, Is.False);
        Assert.That(vm.BrowserStatusText, Is.EqualTo("No drives to display"));
    }

    [Test]
    public async Task InitialFolderDoesNotEnumerateAncestors()
    {
        Assert.That(browser.RequestedDirectories, Is.EqualTo(new[] { a.FullPath }));
    }

    [Test]
    public async Task FlatBrowserShowsContentsAndBreadcrumbs()
    {
        Assert.That(vm.Entries.Single(), Is.SameAs(first));
        Assert.That(vm.Breadcrumbs.First().Name, Is.EqualTo("This PC"));
        Assert.That(vm.Breadcrumbs.Last().FullPath, Is.EqualTo(a.FullPath));
        await vm.NavigateCommand.ExecuteAsync(null);
        Assert.That(vm.IsThisPc, Is.True);
        Assert.That(vm.Entries.Single().IsDirectory, Is.True);
    }

    [TestCase(@"F:\", @"F:\Storage", @"F:\Other")]
    [TestCase(@"F:\", @"f:\Storage", @"F:\Other")]
    [TestCase(@"\\server\share\", @"\\server\share\Storage", @"\\server\share\Other")]
    public async Task NavigationReusesDriveOrUncRoot(string rootPath, string firstPath, string nextPath)
    {
        var root = new SampleBrowserEntry(rootPath, rootPath, true);
        var firstFolder = new SampleBrowserEntry(firstPath, "Storage", true);
        var nextFolder = new SampleBrowserEntry(nextPath, "Other", true);
        var source = new StubSampleBrowserService { RootEntries = [root], DefaultDirectory = firstPath };
        source.Entries[rootPath] = [firstFolder, nextFolder];
        source.Entries[firstPath] = [];
        source.Entries[nextPath] = [];
        var model = new SampleLoaderPopupVM(source, new StubSamplePreviewService(), new ImmediateUiScheduler(), new PlaybackVolumeSettings());
        try
        {
            await model.InitializeAsync(default);
        await model.NavigateCommand.ExecuteAsync(nextPath);
        await model.NavigateCommand.ExecuteAsync(firstPath);
            Assert.That(model.Breadcrumbs.Last().FullPath, Is.EqualTo(firstPath));
            Assert.That(model.CurrentDirectory, Is.EqualTo(firstPath));
            Assert.That(model.DirectoryError, Is.Null);
        }
        finally { await model.DeactivateAsync(); }
    }

    [Test]
    public async Task SiblingPrefixIsNotAnAncestor()
    {
        var sibling = Path.Combine(browser.RootPath, "AExtra");
        var folder = new SampleBrowserEntry(sibling, "AExtra", true);
        browser.Entries[browser.RootPath] = [a, b, folder];
        browser.Entries[sibling] = [];
        await vm.NavigateCommand.ExecuteAsync(sibling);
        Assert.That(vm.Breadcrumbs.Last().FullPath, Is.EqualTo(folder.FullPath));
        Assert.That(vm.Entries, Is.Empty);
        Assert.That(vm.CurrentDirectory, Is.EqualTo(sibling));
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task SuppliedDirectoryOverridesDefaultAndPreservesUnavailablePath(bool unavailable)
    {
        var source = new StubSampleBrowserService();
        var requested = Path.Combine(source.RootPath, "Beatmap");
        source.DefaultDirectory = source.RootPath;
        source.Entries[source.RootPath] = [new(requested, "Beatmap", true)];
        if (!unavailable) source.Entries[requested] = [];
        var model = new SampleLoaderPopupVM(source, new StubSamplePreviewService(), new ImmediateUiScheduler(), new PlaybackVolumeSettings());
        try
        {
            await model.InitializeAsync(requested, default);
            Assert.That(source.DefaultDirectoryRequests, Is.Zero);
            Assert.That(model.IsThisPc, Is.EqualTo(unavailable));
            Assert.That(model.SelectedFile, Is.Null);
            Assert.That(model.CheckedCount, Is.Zero);
            if (unavailable)
            {
                Assert.That(model.DirectoryError, Is.Not.Empty);
                Assert.That(model.CurrentDirectory, Is.Empty);
            }
            else
            {
                Assert.That(model.CurrentDirectory, Is.EqualTo(requested));
                Assert.That(model.DirectoryError, Is.Null);
            }
        }
        finally { await model.DeactivateAsync(); }
    }

    [Test]
    public async Task NavigationClearsChecksButSelectionAndSameFolderDoNot()
    {
        await vm.SetCheckedAsync(first, true);
        await vm.SelectAsync(b);
        Assert.That(first.IsChecked, Is.True);
        await vm.NavigateCommand.ExecuteAsync(a.FullPath);
        Assert.That(first.IsChecked, Is.True);
        await vm.NavigateCommand.ExecuteAsync(b.FullPath);
        Assert.That(first.IsChecked, Is.False);
        Assert.That(vm.CheckedCount, Is.Zero);
    }
    [Test]
    public async Task InvalidPathPreservesCommittedFolderAndChecks()
    {
        await vm.SetCheckedAsync(first, true);
        await vm.NavigateCommand.ExecuteAsync(Path.Combine(browser.RootPath, "Missing"));
        Assert.That(vm.CurrentDirectory, Is.EqualTo(a.FullPath));
        Assert.That(first.IsChecked, Is.True);
        Assert.That(vm.DirectoryError, Is.Not.Empty);
    }
    [Test]
    public async Task CheckingDifferentBranchNavigatesBeforeChecking()
    {
        await vm.SetCheckedAsync(first, true);
        await vm.SetCheckedAsync(second, true);
        Assert.That(first.IsChecked, Is.False);
        Assert.That(vm.CurrentDirectory, Is.EqualTo(b.FullPath));
        Assert.That(await vm.ConfirmAsync(), Is.EqualTo(new[] { second.FullPath }));
    }
    [Test]
    public async Task SelectionLoadsMetadataWithoutTogglingOrPlaying()
    {
        vm.AutoPlaySamples = false;
        await vm.SelectAsync(first);
        Assert.That(vm.Metadata!.Name, Is.EqualTo("first.wav"));
        Assert.That(vm.CheckedCount, Is.Zero);
        Assert.That(vm.IsPreviewPlaying, Is.False);
        Assert.That(vm.CanPreview, Is.True);
    }
    [Test]
    public async Task LateNavigationAndMetadataCannotOverwriteNewSelection()
    {
        var release = new TaskCompletionSource<SampleFileMetadata>();
        browser.Metadata = (path, _) => path == first.FullPath ? release.Task : Task.FromResult(new SampleFileMetadata(path, "second.wav", "WAV", 1000, TimeSpan.FromSeconds(1), 44_100, 1));
        var old = vm.SelectAsync(first);
        await vm.SelectAsync(second);
        release.SetResult(new(first.FullPath, "first.wav", "WAV", 1, TimeSpan.Zero, 1, 1));
        await old;
        Assert.That(vm.Metadata!.Name, Is.EqualTo("second.wav"));
        Assert.That(vm.CurrentDirectory, Is.EqualTo(b.FullPath));
    }
    [Test]
    public async Task PreviewStopsOnSelectionNavigationConfirmAndDeactivate()
    {
        vm.AutoPlaySamples = false;
        await vm.SetCheckedAsync(first, true);
        await vm.TogglePreviewCommand.ExecuteAsync(null);
        Assert.That(vm.IsPreviewPlaying, Is.True);
        await vm.SelectAsync(second);
        Assert.That(vm.IsPreviewPlaying, Is.False);
        await vm.TogglePreviewCommand.ExecuteAsync(null);
        await vm.NavigateCommand.ExecuteAsync(a.FullPath);
        Assert.That(vm.IsPreviewPlaying, Is.False);
        await vm.SetCheckedAsync(first, true);
        await vm.TogglePreviewCommand.ExecuteAsync(null);
        await vm.ConfirmAsync();
        Assert.That(vm.IsPreviewPlaying, Is.False);
        await vm.DeactivateAsync();
        Assert.That(vm.CanImport, Is.False);
        Assert.That(vm.CanPreview, Is.False);
    }
    [Test]
    public async Task DecodeFailureDisablesPlayButKeepsImportAvailable()
    {
        browser.Metadata = (_, _) => Task.FromException<SampleFileMetadata>(new IOException("Corrupt audio"));
        await vm.SetCheckedAsync(first, true);
        Assert.That(vm.MetadataError, Does.Contain("Corrupt"));
        Assert.That(vm.CanPreview, Is.False);
        Assert.That(vm.CanImport, Is.True);
    }
    [Test]
    public async Task ConfirmationReturnsCheckedPathsOnly()
    {
        await vm.SetCheckedAsync(first, true);
        var result = await vm.ConfirmAsync();
        first.IsChecked = false;
        Assert.That(result, Is.EqualTo(new[] { first.FullPath }));
    }
    [Test]
    public async Task DeactivateCancelsPendingPlayAndRejectsLateResults()
    {
        vm.AutoPlaySamples = false;
        var started = new TaskCompletionSource();
        preview.Play = async token => { started.SetResult(); await Task.Delay(Timeout.Infinite, token); };
        await vm.SelectAsync(first);
        var play = vm.TogglePreviewCommand.ExecuteAsync(null);
        await started.Task;
        await vm.DeactivateAsync();
        await play;
        Assert.That(vm.IsPreviewPlaying, Is.False);
        Assert.That(preview.Disposed, Is.True);
    }

    [Test]
    public async Task LateListingCannotReplaceNewerLocation()
    {
        var pending = new TaskCompletionSource<IReadOnlyList<SampleBrowserEntry>>();
        browser.Listing = (path, _) => path == b.FullPath ? pending.Task : Task.FromResult(browser.Entries[path]);
        var obsolete = vm.NavigateCommand.ExecuteAsync(b.FullPath);
        await vm.NavigateCommand.ExecuteAsync(a.FullPath);
        pending.SetResult([second]);
        await obsolete;
        Assert.That(vm.CurrentDirectory, Is.EqualTo(a.FullPath));
        Assert.That(vm.Entries.Single(), Is.SameAs(first));
    }

    [Test]
    public async Task NavigationDoesNotRequireAncestorListings()
    {
        var folder = new SampleBrowserEntry(Path.Combine(browser.RootPath, "New"), "New", true);
        browser.Entries[browser.RootPath] = [a, b, folder];
        browser.Entries[folder.FullPath] = [];
        await vm.NavigateCommand.ExecuteAsync(folder.FullPath);
        Assert.That(vm.CurrentDirectory, Is.EqualTo(folder.FullPath));
        Assert.That(vm.DirectoryError, Is.Null);
        Assert.That(vm.Breadcrumbs.Last().FullPath, Is.EqualTo(folder.FullPath));
    }

    [Test]
    public async Task AutoPlayDefaultsOnAndStartsSelectedSample()
    {
        Assert.That(vm.AutoPlaySamples, Is.True);
        await vm.SelectAsync(first);
        Assert.That(preview.PlayedPaths, Is.EqualTo(new[] { first.FullPath }));
        Assert.That(vm.IsPreviewPlaying, Is.True);
        Assert.That(first.IsChecked, Is.False);
    }

    [Test]
    public async Task DisabledAutoPlayStillLoadsMetadata()
    {
        vm.AutoPlaySamples = false;
        await vm.SelectAsync(first);
        Assert.That(vm.Metadata!.Name, Is.EqualTo(first.Name));
        Assert.That(preview.PlayedPaths, Is.Empty);
        Assert.That(vm.PlaybackDurationSeconds, Is.EqualTo(1));
    }

    [Test]
    public async Task ChangingAutoPlayDoesNotStartOrStopCurrentSample()
    {
        vm.AutoPlaySamples = false;
        await vm.SelectAsync(first);
        vm.AutoPlaySamples = true;
        Assert.That(preview.PlayedPaths, Is.Empty);
        await vm.TogglePreviewCommand.ExecuteAsync(null);
        vm.AutoPlaySamples = false;
        Assert.That(vm.IsPreviewPlaying, Is.True);
    }

    [Test]
    public async Task ManualPlayUsesStoppedSeekPosition()
    {
        vm.AutoPlaySamples = false;
        await vm.SelectAsync(first);
        await vm.SeekAsync(.4);
        Assert.That(vm.PlaybackPositionSeconds, Is.EqualTo(.4));
        Assert.That(preview.SeekPositions, Is.Empty);
        await vm.TogglePreviewCommand.ExecuteAsync(null);
        Assert.That(preview.StartPosition.TotalSeconds, Is.EqualTo(.4));
        await vm.TogglePreviewCommand.ExecuteAsync(null);
        Assert.That(vm.PlaybackPositionSeconds, Is.Zero);
    }

    [Test]
    public async Task SeekAndCompletionResetOnNextPlay()
    {
        await vm.SelectAsync(first);
        await vm.SeekAsync(.6);
        Assert.That(preview.SeekPositions.Single().TotalSeconds, Is.EqualTo(.6));
        await vm.TogglePreviewCommand.ExecuteAsync(null);
        await vm.SeekAsync(5);
        await vm.TogglePreviewCommand.ExecuteAsync(null);
        Assert.That(preview.StartPosition, Is.EqualTo(TimeSpan.Zero));
    }

    [Test]
    public async Task SelectionDuringPendingMetadataPlayOrSeekCannotRestoreStaleState()
    {
        var pending = new TaskCompletionSource<SampleFileMetadata>();
        browser.Metadata = (path, _) => path == first.FullPath ? pending.Task :
            Task.FromResult(new SampleFileMetadata(path, second.Name, "WAV", 1, TimeSpan.FromSeconds(1), 44100, 1));
        var old = vm.SelectAsync(first);
        await vm.SelectAsync(second);
        pending.SetResult(new(first.FullPath, first.Name, "WAV", 1, TimeSpan.FromSeconds(1), 44100, 1));
        await old;
        Assert.That(preview.PlayedPaths, Is.EqualTo(new[] { second.FullPath }));

        var seekStarted = new TaskCompletionSource();
        preview.Seek = async token => { seekStarted.SetResult(); await Task.Delay(Timeout.Infinite, token); };
        var seeking = vm.SeekAsync(.7);
        await seekStarted.Task;
        await vm.NavigateCommand.ExecuteAsync(a.FullPath);
        await seeking;
        Assert.That(vm.SelectedFile, Is.Null);
        Assert.That(vm.PlaybackPositionSeconds, Is.Zero);
        Assert.That(vm.IsSeeking, Is.False);
    }

    [Test]
    public async Task ZeroDurationDisablesSeek()
    {
        browser.Metadata = (path, _) => Task.FromResult(new SampleFileMetadata(path, first.Name, "WAV", 1, TimeSpan.Zero, 44100, 1));
        vm.AutoPlaySamples = false;
        await vm.SelectAsync(first);
        Assert.That(vm.CanSeek, Is.False);
        await vm.SeekAsync(.5);
        Assert.That(vm.PlaybackPositionSeconds, Is.Zero);
    }

    [Test]
    public async Task SeekFailureShowsErrorAndRestoresBusyState()
    {
        await vm.SelectAsync(first);
        preview.Seek = _ => Task.FromException(new IOException("Seeking failed"));
        await vm.SeekAsync(.5);
        Assert.That(vm.PreviewError, Does.Contain("Seeking failed"));
        Assert.That(vm.IsSeeking, Is.False);
        Assert.That(vm.CanPreview, Is.True);
    }

    [Test]
    public async Task EmptyFolderGetsInformationalChild()
    {
        browser.Entries[b.FullPath] = [];
        await vm.NavigateCommand.ExecuteAsync(b.FullPath);
        Assert.That(vm.IsFolderEmpty, Is.True);
        Assert.That(vm.Entries, Is.Empty);
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task RepeatedFileActivationRestartsFromZero(bool playing)
    {
        await vm.ActivateFileAsync(first);
        await vm.SeekAsync(.5);
        if (!playing) await vm.TogglePreviewCommand.ExecuteAsync(null);
        await vm.ActivateFileAsync(first);
        Assert.That(preview.PlayedPaths, Is.EqualTo(new[] { first.FullPath, first.FullPath }));
        Assert.That(preview.StartPosition, Is.EqualTo(TimeSpan.Zero));
        Assert.That(vm.PlaybackPositionSeconds, Is.Zero);
        Assert.That(vm.IsPreviewPlaying, Is.True);
        Assert.That(first.IsChecked, Is.False);
    }

    [Test]
    public async Task DisabledAutoPlayActivationDoesNotPlay()
    {
        vm.AutoPlaySamples = false;
        await vm.ActivateFileAsync(first);
        await vm.SeekAsync(.5);
        await vm.ActivateFileAsync(first);
        Assert.That(vm.SelectedFile, Is.SameAs(first));
        Assert.That(vm.Metadata!.Name, Is.EqualTo(first.Name));
        Assert.That(vm.PlaybackPositionSeconds, Is.EqualTo(.5));
        Assert.That(preview.PlayedPaths, Is.Empty);
    }

    [Test]
    public async Task CheckingSelectedFileDoesNotReplay()
    {
        await vm.ActivateFileAsync(first);
        await vm.SeekAsync(.5);
        await vm.SetCheckedAsync(first, true);
        await vm.SetCheckedAsync(first, true);
        Assert.That(first.IsChecked, Is.True);
        Assert.That(vm.CheckedCount, Is.EqualTo(1));
        Assert.That(vm.PlaybackPositionSeconds, Is.EqualTo(.5));
        Assert.That(preview.PlayedPaths, Is.EqualTo(new[] { first.FullPath }));
    }

    [Test]
    public async Task PendingActivationMetadataCannotRestoreOldSelection()
    {
        var pending = new TaskCompletionSource<SampleFileMetadata>();
        browser.Metadata = (path, _) => path == first.FullPath ? pending.Task :
            Task.FromResult(new SampleFileMetadata(path, second.Name, "WAV", 1000, TimeSpan.FromSeconds(1), 44100, 1));
        var activating = vm.ActivateFileAsync(first);
        await vm.SelectAsync(second);
        pending.SetResult(new(first.FullPath, first.Name, "WAV", 1000, TimeSpan.FromSeconds(1), 44100, 1));
        await activating;
        Assert.That(vm.SelectedFile, Is.SameAs(second));
        Assert.That(vm.Metadata!.Name, Is.EqualTo(second.Name));
        Assert.That(preview.PlayedPaths, Is.EqualTo(new[] { second.FullPath }));
    }

    [Test]
    public async Task PendingReplayCannotRestartSupersedingSelection()
    {
        var snapshotStarted = new TaskCompletionSource();
        var release = new TaskCompletionSource<SamplePreviewSnapshot>();
        preview.Snapshot = _ => { snapshotStarted.TrySetResult(); return release.Task; };
        await vm.ActivateFileAsync(first);
        await snapshotStarted.Task;
        var replaying = vm.ActivateFileAsync(first);
        var selecting = vm.SelectAsync(second);
        preview.Snapshot = null;
        release.SetResult(new(true, null, null, TimeSpan.FromSeconds(.5), TimeSpan.FromSeconds(1)));
        await Task.WhenAll(replaying, selecting).WaitAsync(TimeSpan.FromSeconds(2));
        Assert.That(vm.SelectedFile, Is.SameAs(second));
        Assert.That(vm.Metadata!.Name, Is.EqualTo(second.Name));
        Assert.That(vm.PlaybackPositionSeconds, Is.Zero);
        Assert.That(preview.PlayedPaths, Is.EqualTo(new[] { first.FullPath, second.FullPath }));
    }

    [Test]
    public async Task FolderWithOnlySubfoldersRetainsThemWithoutEmptyMessage()
    {
        var inner = new SampleBrowserEntry(Path.Combine(b.FullPath, "inner"), "inner", true);
        browser.Entries[b.FullPath] = [inner];
        browser.Entries[inner.FullPath] = [new(Path.Combine(inner.FullPath, "hit.wav"), "hit.wav", false)];
        await vm.NavigateCommand.ExecuteAsync(b.FullPath);
        Assert.That(vm.Entries, Does.Contain(inner));
        Assert.That(vm.IsFolderEmpty, Is.False);
        await vm.NavigateCommand.ExecuteAsync(inner.FullPath);
        Assert.That(vm.CurrentDirectory, Is.EqualTo(inner.FullPath));
    }

    [Test]
    public async Task EmptyNestedLeafHasNoContentRows()
    {
        var inner = new SampleBrowserEntry(Path.Combine(b.FullPath, "inner"), "inner", true);
        browser.Entries[b.FullPath] = [inner];
        browser.Entries[inner.FullPath] = [];
        await vm.NavigateCommand.ExecuteAsync(b.FullPath);
        Assert.That(vm.Entries, Is.EqualTo(new[] { inner }));
        await vm.NavigateCommand.ExecuteAsync(inner.FullPath);
        Assert.That(vm.IsFolderEmpty, Is.True);
        await vm.NavigateCommand.ExecuteAsync(inner.FullPath);
        Assert.That(vm.Entries, Is.Empty);
    }

    [Test]
    public async Task EmptyFolderCannotBeImportedOrPreviewed()
    {
        browser.Entries[b.FullPath] = [];
        await vm.NavigateCommand.ExecuteAsync(b.FullPath);
        var message = b;
        await vm.SetCheckedAsync(message, true);
        await vm.SelectAsync(message);
        Assert.That(vm.CheckedCount, Is.Zero);
        Assert.That(vm.SelectedFile, Is.Null);
        Assert.That(vm.BrowserStatusText, Is.Empty);
        Assert.That(preview.PlayedPaths, Is.Empty);
    }

    [Test]
    public async Task PendingPlayCannotStopNewSelection()
    {
        var started = new TaskCompletionSource();
        var release = new TaskCompletionSource();
        preview.Play = _ => { started.TrySetResult(); return release.Task; };
        var selecting = vm.ActivateFileAsync(first);
        await started.Task;
        var next = vm.SelectAsync(second);
        release.SetResult();
        await Task.WhenAll(selecting, next);
        Assert.That(vm.SelectedFile, Is.SameAs(second));
        Assert.That(vm.IsPreviewPlaying, Is.True);
        Assert.That(preview.PlayedPaths, Is.EqualTo(new[] { second.FullPath }));
    }

    [Test]
    public async Task PendingSnapshotDuringSeekAndSelectionCannotReplaceNewPlayback()
    {
        var snapshotStarted = new TaskCompletionSource();
        var release = new TaskCompletionSource<SamplePreviewSnapshot>();
        preview.Snapshot = _ => { snapshotStarted.TrySetResult(); return release.Task; };
        await vm.SelectAsync(first);
        await snapshotStarted.Task;
        var seeking = vm.SeekAsync(.6);
        var busyDuringSnapshot = vm.IsSeeking;
        var selecting = vm.SelectAsync(second);
        preview.Snapshot = null;
        release.SetResult(new(true, null, null, TimeSpan.FromSeconds(.8), TimeSpan.FromSeconds(1)));
        await Task.WhenAll(seeking, selecting).WaitAsync(TimeSpan.FromSeconds(2));
        Assert.That(busyDuringSnapshot, Is.True);
        Assert.That(vm.SelectedFile, Is.SameAs(second));
        Assert.That(vm.IsPreviewPlaying, Is.True);
        Assert.That(vm.PlaybackPositionSeconds, Is.Zero);
        await vm.NavigateCommand.ExecuteAsync(a.FullPath);
        Assert.That(vm.IsPreviewPlaying, Is.False);
    }

    [Test]
    public async Task NaturalCompletionShowsEndAndNextPlayStartsAtZero()
    {
        var completed = new TaskCompletionSource();
        preview.Snapshot = _ => Task.FromResult(new SamplePreviewSnapshot(false, null, null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1)));
        vm.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(vm.PlaybackPositionSeconds) && vm.PlaybackPositionSeconds == 1) completed.TrySetResult();
        };
        await vm.SelectAsync(first);
        await completed.Task;
        Assert.That(vm.IsPreviewPlaying, Is.False);
        preview.Snapshot = null;
        await vm.TogglePreviewCommand.ExecuteAsync(null);
        Assert.That(preview.StartPosition, Is.EqualTo(TimeSpan.Zero));
    }
}
