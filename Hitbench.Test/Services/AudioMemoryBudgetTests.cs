using Hitbench.Services;

namespace Hitbench.Test.Services;

public class AudioMemoryBudgetTests
{
    [Test]
    public void ExclusiveDecodedResultCanTransferBudgetWithoutCopying()
    {
        var first = new AudioMemoryBudget(64, 64);
        var second = new AudioMemoryBudget(64, 64);
        using var audio = DecodedAudio.CopyFrom(new float[16], first);
        audio.AttachBudget(second);
        Assert.That(first.UsedBytes, Is.Zero);
        Assert.That(second.UsedBytes, Is.EqualTo(64));
        using var retained = audio.Retain();
        Assert.Throws<InvalidOperationException>(() => audio.AttachBudget(first));
        Assert.That(second.UsedBytes, Is.EqualTo(64));
    }

    [Test]
    public void SharedHandlesChargeOnceAndReleaseDeterministically()
    {
        var budget = new AudioMemoryBudget(64, 64);
        var owner = DecodedAudio.CopyFrom(new float[16], budget);
        var borrowed = owner.Retain();
        Assert.That(budget.UsedBytes, Is.EqualTo(64));
        Assert.Throws<AudioMemoryLimitException>(() => budget.Reserve(1));
        owner.Dispose(); owner.Dispose();
        Assert.That(borrowed.FrameCount, Is.EqualTo(8));
        Assert.That(budget.UsedBytes, Is.EqualTo(64));
        borrowed.Dispose();
        Assert.That(budget.UsedBytes, Is.Zero);
    }

    [Test]
    public void DefaultsAndRejectedCopyRespectBudget()
    {
        var defaults = new AudioMemoryBudget();
        Assert.That(defaults.MaximumBytes, Is.EqualTo(536870912));
        Assert.That(defaults.MaximumAssetBytes, Is.EqualTo(268435456));
        var budget = new AudioMemoryBudget(8, 8);
        Assert.Throws<AudioMemoryLimitException>(() => DecodedAudio.CopyFrom(new float[4], budget));
        Assert.That(budget.UsedBytes, Is.Zero);
    }

    [Test]
    public void ParallelReservationsNeverExceedCeiling()
    {
        var budget = new AudioMemoryBudget(1024, 1024);
        Parallel.For(0, 200, _ => { try { using var charge = budget.Reserve(512); Thread.Yield(); } catch (AudioMemoryLimitException) { } });
        Assert.That(budget.UsedBytes, Is.Zero);
        Assert.That(budget.PeakBytes, Is.LessThanOrEqualTo(1024));
    }
}
