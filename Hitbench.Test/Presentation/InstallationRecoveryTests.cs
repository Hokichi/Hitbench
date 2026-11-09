using CommunityToolkit.Mvvm.Messaging;
using Hitbench.Core.Entities;
using Hitbench.Resources.DataModels;
using Hitbench.ViewModels.Shell.HomePage;
namespace Hitbench.Test.Presentation;
public class InstallationRecoveryTests
{
    private static HomePageVM Create(StubHomeFolderService folders, StubAppSettingsService settings, StubSampleFileDialogService? picker = null) =>
        new(folders, settings, new StubInstallationDialogService { Locate = _ => throw new AssertionException("Native picker must not open") }, new ImmediateUiScheduler(),
            new StubProjectFileService((_, _, _) => Task.FromResult("saved.json")), new WeakReferenceMessenger(), picker: picker);
    [Test]
    public async Task SavedRootSkipsExecutableScan()
    {
        string? loaded = null;
        var vm = Create(new StubHomeFolderService { Find = _ => throw new AssertionException("Must not scan"), Songs = (path, _) => { loaded = path; return Task.FromResult<FolderSnapshot?>(new FolderSnapshot([])); } }, new StubAppSettingsService { Save = (_, _) => throw new AssertionException("Must not overwrite installation") });
        try { await vm.LoadAsync(default); Assert.That(loaded, Is.EqualTo(@"C:\osu\Songs")); Assert.That(vm.MapsetsAvailability, Is.EqualTo(OHsHomeMapsetsAvailability.Empty)); }
        finally { vm.Deactivate(); }
    }
    [Test]
    public async Task FirstLaunchScanFailureWaitsForExplicitLocate()
    {
        var picker = new StubSampleFileDialogService(@"C:\chosen");
        var saved = "";
        var vm = Create(new StubHomeFolderService(), new StubAppSettingsService { Load = _ => Task.FromResult(new AppSettings()), SaveMapsetsDirectory = (path, _) => { saved = path; return Task.CompletedTask; } }, picker);
        try {
            await vm.LoadAsync(default);
            Assert.That(picker.Context, Is.Null); Assert.That(vm.MapsetsAvailability, Is.EqualTo(OHsHomeMapsetsAvailability.NotFound));
            await vm.LocateOsuFolderCommand.ExecuteAsync(null);
            Assert.That(saved, Is.EqualTo(@"C:\chosen\Songs")); Assert.That(picker.Context!.FolderPurpose, Is.EqualTo(OHsFolderSelectionPurpose.OsuInstallation));
        } finally { vm.Deactivate(); }
    }
    [Test]
    public async Task EmptySettingScansAndPersistsDetectedSongsRoot()
    {
        var saved = "";
        var installation = "";
        var pending = new TaskCompletionSource();
        var songsLoaded = false;
        var vm = Create(new StubHomeFolderService
        {
            Find = _ => Task.FromResult(new OsuSearchResult(@"C:\detected", false)),
            Songs = (_, _) => { songsLoaded = true; return Task.FromResult<FolderSnapshot?>(new FolderSnapshot([])); }
        }, new StubAppSettingsService
        {
            Load = _ => Task.FromResult(new AppSettings()),
            Save = (path, _) => { installation = path; return pending.Task; },
            SaveMapsetsDirectory = (path, _) => { saved = path; return Task.CompletedTask; }
        });
        try
        {
            var loading = vm.LoadAsync(default);
            Assert.That(installation, Is.EqualTo(@"C:\detected"));
            Assert.That(songsLoaded, Is.False);
            pending.SetResult();
            await loading;
            Assert.That(saved, Is.EqualTo(@"C:\detected\Songs"));
            Assert.That(songsLoaded, Is.True);
        }
        finally { pending.TrySetResult(); vm.Deactivate(); }
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task MissingOrInvalidDetectionDoesNotSaveInstallation(bool invalid)
    {
        var saves = 0;
        var vm = Create(new StubHomeFolderService
        {
            Find = _ => Task.FromResult(new OsuSearchResult(invalid ? @"C:\invalid" : null, false)),
            Valid = (_, _) => Task.FromResult(false)
        }, new StubAppSettingsService
        {
            Load = _ => Task.FromResult(new AppSettings()),
            Save = (_, _) => { saves++; return Task.CompletedTask; },
            SaveMapsetsDirectory = (_, _) => { saves++; return Task.CompletedTask; }
        });
        try { await vm.LoadAsync(default); Assert.That(saves, Is.Zero); }
        finally { vm.Deactivate(); }
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task FailedOrCancelledInstallationSaveDoesNotLoadSongs(bool cancel)
    {
        using var cancellation = new CancellationTokenSource();
        var songsLoaded = false;
        var mapsetsSaved = false;
        var vm = Create(new StubHomeFolderService
        {
            Find = _ => Task.FromResult(new OsuSearchResult(@"C:\detected", false)),
            Songs = (_, _) => { songsLoaded = true; return Task.FromResult<FolderSnapshot?>(new FolderSnapshot([])); }
        }, new StubAppSettingsService
        {
            Load = _ => Task.FromResult(new AppSettings()),
            Save = (_, _) =>
            {
                if (!cancel) throw new IOException("Settings write failed");
                cancellation.Cancel();
                return Task.CompletedTask;
            },
            SaveMapsetsDirectory = (_, _) => { mapsetsSaved = true; return Task.CompletedTask; }
        });
        try
        {
            if (cancel) Assert.CatchAsync<OperationCanceledException>(() => vm.LoadAsync(cancellation.Token));
            else
            {
                await vm.LoadAsync(default);
                Assert.That(vm.CanRetrySongs, Is.True);
                Assert.That(vm.SongsStatus, Does.Contain("Settings write failed"));
            }
            Assert.That(songsLoaded, Is.False);
            Assert.That(mapsetsSaved, Is.False);
        }
        finally { vm.Deactivate(); }
    }
    [Test]
    public async Task CancelledPickerPreservesSavedRoot()
    {
        var picker = new StubSampleFileDialogService((string?)null);
        var vm = Create(new StubHomeFolderService(), new StubAppSettingsService(), picker);
        try { await vm.LoadAsync(default); vm.CompleteSongsPresentation(vm.SongsPresentationGeneration); await vm.LocateMapsetsDirectoryCommand.ExecuteAsync(null); Assert.That(vm.MapsetsDirectory, Is.EqualTo(@"C:\osu\Songs")); Assert.That(picker.InitialDirectory, Is.EqualTo(vm.MapsetsDirectory)); }
        finally { vm.Deactivate(); }
    }
    [Test]
    public async Task CustomRootPersistsAndZeroMatchesRemainAvailable()
    {
        var picker = new StubSampleFileDialogService(@"C:\custom");
        var saved = "";
        var vm = Create(new StubHomeFolderService { Songs = (_, _) => Task.FromResult<FolderSnapshot?>(new FolderSnapshot([new FolderEntry(@"C:\custom\one", "one", default)])) }, new StubAppSettingsService { SaveMapsetsDirectory = (path, _) => { saved = path; return Task.CompletedTask; } }, picker);
        try {
            await vm.LoadAsync(default); vm.CompleteSongsPresentation(vm.SongsPresentationGeneration);
            await vm.LocateMapsetsDirectoryCommand.ExecuteAsync(null);
            vm.CompleteSongsPresentation(vm.SongsPresentationGeneration);
            Assert.That(saved, Is.EqualTo(@"C:\custom")); Assert.That(vm.MapsetsDirectory, Is.EqualTo(saved));
            vm.SongSearchText = "no match"; await vm.SearchSongsCommand.ExecutionTask!;
            Assert.That(vm.Songs, Is.Empty); Assert.That(vm.MapsetsAvailability, Is.EqualTo(OHsHomeMapsetsAvailability.Available));
        } finally { vm.Deactivate(); }
    }
    [Test]
    public async Task MissingSavedRootDoesNotScanOrOverwriteSettings()
    {
        var vm = Create(new StubHomeFolderService { Find = _ => throw new AssertionException("No scan"), Songs = (_, _) => Task.FromResult<FolderSnapshot?>(null) }, new StubAppSettingsService { SaveMapsetsDirectory = (_, _) => throw new AssertionException("No save") });
        try { await vm.LoadAsync(default); Assert.That(vm.MapsetsAvailability, Is.EqualTo(OHsHomeMapsetsAvailability.Empty)); } finally { vm.Deactivate(); }
    }
    [Test]
    public async Task LatePickerResultAfterDeactivationCannotSave()
    {
        var result = new TaskCompletionSource<IReadOnlyList<string>>();
        var picker = new StubSampleFileDialogService((string?)null) { Selection = _ => result.Task };
        var vm = Create(new StubHomeFolderService(), new StubAppSettingsService(), picker);
        await vm.LoadAsync(default); vm.CompleteSongsPresentation(vm.SongsPresentationGeneration);
        var selecting = vm.LocateMapsetsDirectoryCommand.ExecuteAsync(null);
        Assert.That(vm.ReloadSongsCommand.CanExecute(null), Is.False);
        vm.Deactivate(); result.SetResult([@"C:\late"]); await selecting;
        Assert.That(vm.MapsetsDirectory, Is.Not.EqualTo(@"C:\late"));
    }

    [Test]
    public async Task ValidInstallationWithoutSongsShowsEmptyState()
    {
        var vm = Create(new StubHomeFolderService { Songs = (_, _) => Task.FromResult<FolderSnapshot?>(null) }, new StubAppSettingsService { Load = _ => Task.FromResult(new AppSettings()), SaveMapsetsDirectory = (_, _) => throw new AssertionException("Missing root must not save") }, new StubSampleFileDialogService(@"C:\chosen"));
        try { await vm.LoadAsync(default); await vm.LocateOsuFolderCommand.ExecuteAsync(null); Assert.That(vm.MapsetsAvailability, Is.EqualTo(OHsHomeMapsetsAvailability.Empty)); }
        finally { vm.Deactivate(); }
    }

}
