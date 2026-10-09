using osuHitsounder.Resources.DataModels;
using osuHitsounder.Services;
using osuHitsounder.ViewModels.Popups;

namespace osuHitsounder.Test.Presentation;

public sealed class SampleLoaderPopupVMTests
{
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
    public async Task NavigationClearsChecksButExpansionAndSameFolderDoNot()
    {
        await vm.SetCheckedAsync(first, true);
        await vm.ExpandAsync(b);
        Assert.That(first.IsChecked, Is.True);
        vm.DirectoryText = a.FullPath;
        await vm.NavigateCommand.ExecuteAsync(null);
        Assert.That(first.IsChecked, Is.True);
        await vm.SelectAsync(b);
        Assert.That(first.IsChecked, Is.False);
        Assert.That(vm.CheckedCount, Is.Zero);
    }
    [Test]
    public async Task InvalidPathPreservesCommittedFolderAndChecks()
    {
        await vm.SetCheckedAsync(first, true);
        vm.DirectoryText = Path.Combine(browser.RootPath, "Missing");
        await vm.NavigateCommand.ExecuteAsync(null);
        Assert.That(vm.CurrentDirectory, Is.EqualTo(a.FullPath));
        Assert.That(first.IsChecked, Is.True);
        Assert.That(vm.DirectoryError, Is.Not.Empty);
    }
    [Test]
    public async Task CheckingDifferentBranchNavigatesBeforeChecking()
    {
        await vm.SetCheckedAsync(first, true);
        await vm.ExpandAsync(b);
        await vm.SetCheckedAsync(second, true);
        Assert.That(first.IsChecked, Is.False);
        Assert.That(vm.CurrentDirectory, Is.EqualTo(b.FullPath));
        Assert.That(await vm.ConfirmAsync(), Is.EqualTo(new[] { second.FullPath }));
    }
    [Test]
    public async Task SelectionLoadsMetadataWithoutTogglingOrPlaying()
    {
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
        await vm.SetCheckedAsync(first, true);
        await vm.TogglePreviewCommand.ExecuteAsync(null);
        Assert.That(vm.IsPreviewPlaying, Is.True);
        await vm.SelectAsync(second);
        Assert.That(vm.IsPreviewPlaying, Is.False);
        await vm.TogglePreviewCommand.ExecuteAsync(null);
        await vm.SelectAsync(a);
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
    public async Task LateAncestorListingCannotReplaceCurrentTree()
    {
        var pending = new TaskCompletionSource<IReadOnlyList<SampleBrowserEntry>>();
        vm.Roots.Single().IsLoaded = false;
        browser.Listing = (path, _) => path == browser.RootPath ? pending.Task : Task.FromResult(browser.Entries[path]);
        var obsolete = vm.SelectAsync(b);
        await vm.SelectAsync(a);
        pending.SetResult([new(a.FullPath, "A", true), new(b.FullPath, "B", true)]);
        await obsolete;
        Assert.That(vm.Roots.Single().Children[0], Is.SameAs(a));
        Assert.That(vm.CurrentDirectory, Is.EqualTo(a.FullPath));
    }

    [Test]
    public async Task TypedNewFolderRefreshesItsAlreadyLoadedParent()
    {
        var folder = new SampleBrowserEntry(Path.Combine(browser.RootPath, "New"), "New", true);
        browser.Entries[browser.RootPath] = [a, b, folder];
        browser.Entries[folder.FullPath] = [];
        vm.DirectoryText = folder.FullPath;
        await vm.NavigateCommand.ExecuteAsync(null);
        Assert.That(vm.CurrentDirectory, Is.EqualTo(folder.FullPath));
        Assert.That(vm.DirectoryError, Is.Null);
        Assert.That(vm.Roots.Single().Children, Does.Contain(folder));
    }
}
