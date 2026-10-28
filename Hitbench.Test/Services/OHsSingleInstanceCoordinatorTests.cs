using Hitbench.Services.Ui;

namespace Hitbench.Test.Services;

public class OHsSingleInstanceCoordinatorTests
{
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
