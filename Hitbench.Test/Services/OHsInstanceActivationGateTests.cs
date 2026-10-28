using Hitbench.Services.Ui;
using Hitbench.Test.Presentation;

namespace Hitbench.Test.Services;

public class OHsInstanceActivationGateTests
{
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
