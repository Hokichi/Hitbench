using Hitbench.Test.Services;
using Hitbench.ViewModels.Shell;

namespace Hitbench.Test.Presentation;

public sealed class OHsUpdateVMTests
{
    [Test]
    public async Task RepeatedDownloadDoesNotStartConcurrentModal()
    {
        var pending = new TaskCompletionSource<Hitbench.Resources.DataModels.OHsStagedUpdate?>();
        var presentations = 0;
        var vm = new OHsUpdateVM(new StubOHsUpdateService { Check = _ => Task.FromResult<Hitbench.Resources.DataModels.OHsUpdateRelease?>(OHsUpdateTestData.Candidate(100)) },
            new StubOHsUpdateDialogService { Present = (_, _) => { presentations++; return pending.Task; } }, new StubOHsUpdateRestartService());
        await vm.CheckAsync(default);
        var first = vm.DownloadCommand.ExecuteAsync(null);
        await vm.DownloadCommand.ExecuteAsync(null);
        Assert.That(presentations, Is.EqualTo(1));
        Assert.That(vm.CanDownload, Is.False);
        pending.SetResult(null);
        await first;
        Assert.That(vm.CanDownload, Is.True);
        await vm.DeactivateAsync();
    }
    [Test]
    public async Task DeactivatedOwnerCannotStartDiscovery()
    {
        var vm = new OHsUpdateVM(new StubOHsUpdateService { Check = _ => throw new AssertionException("Inactive network request.") }, new StubOHsUpdateDialogService(), new StubOHsUpdateRestartService());
        await vm.DeactivateAsync();
        await vm.CheckAsync(default);
        Assert.That(vm.IsUpdateAvailable, Is.False);
    }
    [Test]
    public async Task ChecksOnceAndCancelledDialogNeverRestarts()
    {
        var checks = 0;
        var service = new StubOHsUpdateService { Check = _ => { checks++; return Task.FromResult<Hitbench.Resources.DataModels.OHsUpdateRelease?>(OHsUpdateTestData.Candidate(100)); } };
        var restart = new StubOHsUpdateRestartService { Restart = (_, _) => throw new AssertionException("Cancelled download requested restart.") };
        var vm = new OHsUpdateVM(service, new StubOHsUpdateDialogService(), restart);
        await vm.CheckAsync(default);
        await vm.CheckAsync(default);
        Assert.That(checks, Is.EqualTo(1));
        Assert.That(vm.IsUpdateAvailable, Is.True);
        await vm.DownloadCommand.ExecuteAsync(null);
        Assert.That(vm.IsBusy, Is.False);
        Assert.That(vm.CanDownload, Is.True);
        await vm.DeactivateAsync();
        Assert.That(vm.CanDownload, Is.False);
    }
    [Test]
    public async Task DiscoveryFailureDoesNotEscapeOrExposeUpdate()
    {
        var vm = new OHsUpdateVM(new StubOHsUpdateService { Check = _ => throw new IOException("offline") }, new StubOHsUpdateDialogService(), new StubOHsUpdateRestartService());
        await vm.CheckAsync(default);
        Assert.That(vm.IsUpdateAvailable, Is.False);
    }
}
