using CommunityToolkit.Mvvm.Messaging;
using Hitbench.Core.Entities;
using Hitbench.Resources.DataModels;
using Hitbench.ViewModels.Shell.HomePage;

namespace Hitbench.Test.Presentation;

public class InstallationRecoveryTests
{
    private static HomePageVM Create(StubHomeFolderService folders, StubAppSettingsService settings,
        StubInstallationDialogService dialogs, ImmediateUiScheduler? scheduler = null) =>
        new(folders, settings, dialogs, scheduler ?? new ImmediateUiScheduler(),
            new StubProjectFileService((_, _, _) => Task.FromResult("saved.json")), new WeakReferenceMessenger());

    [Test]
    public async Task InvalidConfiguredPathNeverScansOrShowsPicker()
    {
        var folders = new StubHomeFolderService
        {
            Valid = (_, _) => Task.FromResult(false),
            Find = _ => throw new AssertionException("Configured paths must not scan")
        };
        var vm = Create(folders, new StubAppSettingsService(), new StubInstallationDialogService
        {
            Locate = _ => throw new AssertionException("Configured paths must not open picker")
        });
        await vm.LoadAsync(CancellationToken.None);
        Assert.That(vm.FatalError, Does.Contain("osuPath"));
        Assert.That(vm.RequiresShutdown, Is.True);
        Assert.That(vm.IsProcessing, Is.False);
    }

    [Test]
    public async Task MissingPathRecoversAndSavesBeforeLoadingSongs()
    {
        var calls = new List<string>();
        var folders = new StubHomeFolderService
        {
            Find = _ => Task.FromResult(new OsuSearchResult(null, false)),
            Songs = (path, _) => { calls.Add("songs:" + path); return Task.FromResult<FolderSnapshot?>(new FolderSnapshot([])); }
        };
        var settings = new StubAppSettingsService
        {
            Load = _ => Task.FromResult(new AppSettings()),
            Save = (path, _) => { calls.Add("save:" + path); return Task.CompletedTask; }
        };
        var vm = Create(folders, settings, new StubInstallationDialogService
        {
            Locate = _ => { calls.Add("picker"); return Task.FromResult<string?>(@"C:\chosen"); }
        });
        await vm.LoadAsync(CancellationToken.None);
        Assert.That(calls, Is.EqualTo(new[] { "picker", @"save:C:\chosen", @"songs:C:\chosen" }));
        Assert.That(vm.RequiresShutdown, Is.False);
        Assert.That(vm.IsInteractionBlocked, Is.True);
        vm.CompleteSongsPresentation(vm.SongsPresentationGeneration);
        Assert.That(vm.IsInteractionBlocked, Is.False);
    }

    [Test]
    public async Task InvalidSelectionWaitsForMessageDismissalBeforeShutdown()
    {
        var dismissed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var shown = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var vm = Create(new StubHomeFolderService { Valid = (_, _) => Task.FromResult(false) },
            new StubAppSettingsService { Load = _ => Task.FromResult(new AppSettings()),
                Save = (_, _) => throw new AssertionException("Invalid path must not save") },
            new StubInstallationDialogService
            {
                Locate = _ => Task.FromResult<string?>(@"C:\bad"),
                NotFound = _ => { shown.SetResult(); return dismissed.Task; }
            });
        var loading = vm.LoadAsync(CancellationToken.None);
        await shown.Task;
        Assert.That(vm.RequiresShutdown, Is.False);
        Assert.That(vm.IsInteractionBlocked, Is.True);
        dismissed.SetResult();
        await loading;
        Assert.That(vm.RequiresShutdown, Is.True);
        Assert.That(vm.FatalError, Is.Null);
    }

    [Test]
    public async Task OverlayPrecedesScanAndCancellationRejectsLateResult()
    {
        var scan = new TaskCompletionSource<OsuSearchResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var rendered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var folders = new StubHomeFolderService
        {
            Find = _ => { started.SetResult(); return scan.Task; }
        };
        var vm = Create(folders,
            new StubAppSettingsService { Load = _ => Task.FromResult(new AppSettings()),
                Save = (_, _) => throw new AssertionException("Cancelled scan must not save") },
            new StubInstallationDialogService { Locate = _ => throw new AssertionException("Cancelled scan must not recover") },
            new ImmediateUiScheduler { Yield = token => rendered.Task.WaitAsync(token) });
        using var cancellation = new CancellationTokenSource();
        var load = vm.LoadAsync(cancellation.Token);
        Assert.That(vm.IsProcessing, Is.True);
        Assert.That(started.Task.IsCompleted, Is.False);
        rendered.SetResult();
        await started.Task;
        cancellation.Cancel();
        scan.SetResult(new OsuSearchResult(@"C:\late", false));
        Assert.CatchAsync<OperationCanceledException>(async () => await load);
        Assert.That(vm.Songs, Is.Empty);
    }

    [Test]
    public async Task PickerCancellationRequestsShutdownWithoutErrorMessage()
    {
        var vm = Create(new StubHomeFolderService(),
            new StubAppSettingsService { Load = _ => Task.FromResult(new AppSettings()) },
            new StubInstallationDialogService { NotFound = _ => throw new AssertionException("No error on cancel") });
        await vm.LoadAsync(CancellationToken.None);
        Assert.That(vm.RequiresShutdown, Is.True);
        Assert.That(vm.FatalError, Is.Null);
    }
}

