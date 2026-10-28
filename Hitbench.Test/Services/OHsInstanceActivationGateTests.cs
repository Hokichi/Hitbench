using Hitbench.Services.Ui;
using Hitbench.Test.Presentation;

namespace Hitbench.Test.Services;

public class OHsInstanceActivationGateTests
{
    [Test]
    public async Task BusyWindowActivationDoesNotBlockFurtherRequests()
    {
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var gate = new OHsInstanceActivationGate(new ImmediateUiScheduler(), () =>
        { entered.Set(); release.Wait(TimeSpan.FromSeconds(8)); });
        gate.MarkReady(); gate.Request();
        var activation = gate.Completion;
        Assert.That(await Task.Run(() => entered.Wait(TimeSpan.FromSeconds(3))), Is.True);
        var request = Task.Run(gate.Request);
        try { await request.WaitAsync(TimeSpan.FromSeconds(2)); }
        finally
        {
            release.Set(); gate.Deactivate();
            await Task.WhenAll(activation, request).WaitAsync(TimeSpan.FromSeconds(3));
        }
    }

    [Test]
    public async Task RequestsBeforeReadyCoalesceAndLateRequestsDoNothing()
    {
        var calls = 0;
        var gate = new OHsInstanceActivationGate(new ImmediateUiScheduler(), () => calls++);
        for (var i = 0; i < 100; i++) gate.Request();
        Assert.That(calls, Is.Zero);
        gate.MarkReady();
        await gate.Completion;
        Assert.That(calls, Is.EqualTo(1));
        gate.Deactivate();
        gate.Request();
        Assert.That(calls, Is.EqualTo(1));
    }
}
