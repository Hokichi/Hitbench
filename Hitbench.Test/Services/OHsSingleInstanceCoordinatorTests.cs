using Hitbench.Services.Ui;

namespace Hitbench.Test.Services;

public class OHsSingleInstanceCoordinatorTests
{
    [Test, NonParallelizable]
    public async Task LaunchDirectoryDoesNotChangeOwnershipOrCauseSecondaryStartupWrites()
    {
        var root = Directory.CreateTempSubdirectory();
        var previous = Environment.CurrentDirectory;
        var identity = "Hitbench.Test." + Guid.NewGuid().ToString("N");
        using var first = new OHsSingleInstanceCoordinator(identity, _ => { });
        using var second = new OHsSingleInstanceCoordinator(identity, _ => { });
        var writes = 0;
        try
        {
            Environment.CurrentDirectory = Directory.CreateDirectory(Path.Combine(root.FullName, "first")).FullName;
            Assert.That(await first.RunPrimaryOrActivateAsync(_ => { writes++; return Task.CompletedTask; }, () => { }, default), Is.True);
            Environment.CurrentDirectory = Directory.CreateDirectory(Path.Combine(root.FullName, "second")).FullName;
            Assert.That(await second.RunPrimaryOrActivateAsync(_ => { writes++; return Task.CompletedTask; }, () => { }, default), Is.False);
            Assert.That(writes, Is.EqualTo(1));
        }
        finally { Environment.CurrentDirectory = previous; root.Delete(true); }
    }

    [Test]
    public async Task ClosingActivationChannelDoesNotReleaseLifetimeOwnership()
    {
        var identity = "Hitbench.Test." + Guid.NewGuid().ToString("N");
        using var owner = new OHsSingleInstanceCoordinator(identity, _ => { });
        await owner.RunPrimaryOrActivateAsync(_ => Task.CompletedTask, () => { }, default);
        owner.StopActivation();
        using var contender = new OHsSingleInstanceCoordinator(identity, _ => { }, TimeSpan.FromMilliseconds(100));
        var writes = 0;
        Assert.ThrowsAsync<IOException>(() => contender.RunPrimaryOrActivateAsync(_ => { writes++; return Task.CompletedTask; }, () => { }, default));
        Assert.That(writes, Is.Zero);
        owner.Dispose();
        Assert.That(await contender.RunPrimaryOrActivateAsync(_ => Task.CompletedTask, () => { }, default), Is.True);
    }

    [Test]
    public async Task AbandonedMutexCanBeRecovered()
    {
        var identity = "Global\\Hitbench.Test." + Guid.NewGuid().ToString("N");
        using var mutex = new Mutex(false, identity);
        var thread = new Thread(() => mutex.WaitOne());
        thread.Start(); thread.Join();
        using var recovered = await OHsMachineInstanceLease.TryAcquireAsync(identity, default);
        Assert.That(recovered, Is.Not.Null);
    }

    [Test]
    public async Task UnreachableLiveOwnerFailsClosedWithinDeadline()
    {
        var identity = "Hitbench.Test." + Guid.NewGuid().ToString("N");
        using var owner = await OHsMachineInstanceLease.TryAcquireAsync("Global\\" + identity, default);
        using var secondary = new OHsSingleInstanceCoordinator(identity, _ => { }, TimeSpan.FromMilliseconds(100));
        var writes = 0;
        Assert.ThrowsAsync<IOException>(() => secondary.RunPrimaryOrActivateAsync(_ => { writes++; return Task.CompletedTask; }, () => { }, default));
        Assert.That(writes, Is.Zero);
    }

    [Test]
    public async Task SimultaneousLaunchesInitializeOnlyOneOwner()
    {
        var identity = "Hitbench.Test." + Guid.NewGuid().ToString("N");
        using var first = new OHsSingleInstanceCoordinator(identity, _ => { });
        using var second = new OHsSingleInstanceCoordinator(identity, _ => { });
        var writes = 0;
        var activated = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var results = await Task.WhenAll(
            first.RunPrimaryOrActivateAsync(_ => { Interlocked.Increment(ref writes); return Task.CompletedTask; }, () => activated.TrySetResult(), default),
            second.RunPrimaryOrActivateAsync(_ => { Interlocked.Increment(ref writes); return Task.CompletedTask; }, () => activated.TrySetResult(), default));
        Assert.That(results.Count(owner => owner), Is.EqualTo(1));
        await activated.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.That(writes, Is.EqualTo(1));
    }

    [Test]
    public async Task FailedInitializerReleasesOwnership()
    {
        var identity = "Hitbench.Test." + Guid.NewGuid().ToString("N");
        using var first = new OHsSingleInstanceCoordinator(identity, _ => { });
        Assert.ThrowsAsync<IOException>(() => first.RunPrimaryOrActivateAsync(_ => throw new IOException("startup"), () => { }, default));
        using var next = new OHsSingleInstanceCoordinator(identity, _ => { });
        Assert.That(await next.RunPrimaryOrActivateAsync(_ => Task.CompletedTask, () => { }, default), Is.True);
    }

    [Test]
    public async Task MutexOwnershipSurvivesAwaitAndReleasesFromItsOwnThread()
    {
        var identity = "Global\\Hitbench.Test." + Guid.NewGuid().ToString("N");
        var owner = await OHsMachineInstanceLease.TryAcquireAsync(identity, default);
        Assert.That(owner, Is.Not.Null);
        Assert.That(await OHsMachineInstanceLease.TryAcquireAsync(identity, default), Is.Null);
        await Task.Yield();
        owner!.Dispose();
        using var successor = await OHsMachineInstanceLease.TryAcquireAsync(identity, default);
        Assert.That(successor, Is.Not.Null);
    }
}
