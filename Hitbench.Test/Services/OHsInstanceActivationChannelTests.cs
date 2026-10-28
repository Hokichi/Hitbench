using System.IO.Pipes;
using Hitbench.Services.Ui;

namespace Hitbench.Test.Services;

public class OHsInstanceActivationChannelTests
{
    [Test]
    public async Task MalformedClientDoesNotPreventNextActivation()
    {
        var identity = "Hitbench.Test." + Guid.NewGuid().ToString("N");
        using var channel = new OHsInstanceActivationChannel(identity, _ => { });
        var calls = 0;
        channel.Start(() => calls++);
        using (var malformed = new NamedPipeClientStream(".", identity, PipeDirection.InOut, PipeOptions.Asynchronous))
        {
            await malformed.ConnectAsync(2000);
            var hello = new byte[5];
            await malformed.ReadExactlyAsync(hello);
            await malformed.WriteAsync(new byte[] { 99 });
        }
        await channel.RequestAsync(TimeSpan.FromSeconds(3), default);
        Assert.That(calls, Is.EqualTo(1));
    }

    [Test]
    public void MissingEndpointHonorsCancellation()
    {
        using var channel = new OHsInstanceActivationChannel("Hitbench.Test." + Guid.NewGuid().ToString("N"), _ => { });
        using var cancel = new CancellationTokenSource();
        cancel.Cancel();
        Assert.CatchAsync<OperationCanceledException>(() => channel.RequestAsync(TimeSpan.FromSeconds(1), cancel.Token));
    }
}
