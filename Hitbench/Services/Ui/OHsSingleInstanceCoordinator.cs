using System.IO;

namespace Hitbench.Services.Ui;

public sealed class OHsSingleInstanceCoordinator(string identity = "Hitbench.Application.Activation", Action<uint>? grantForeground = null) : IDisposable
{
    public const string MachineIdentity = "Global\\Hitbench.Application.Instance";
    private OHsMachineInstanceLease? lease;
    private OHsInstanceActivationChannel? channel;

    public async Task<bool> RunPrimaryOrActivateAsync(Func<CancellationToken, Task> initializePrimary, Action requestActivation, CancellationToken token)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        while (true)
        {
            token.ThrowIfCancellationRequested();
            lease = await OHsMachineInstanceLease.TryAcquireAsync(identity == "Hitbench.Application.Activation" ? MachineIdentity : "Global\\" + identity, token);
            if (lease is not null)
            {
                try
                {
                    channel = new(identity, grantForeground);
                    channel.Start(requestActivation);
                    await initializePrimary(token);
                    return true;
                }
                catch { Dispose(); throw; }
            }
            using var client = new OHsInstanceActivationChannel(identity, grantForeground);
            try
            {
                await client.RequestAsync(TimeSpan.FromMilliseconds(200), token);
                return false;
            }
            catch (Exception error) when (error is IOException || error is OperationCanceledException && !token.IsCancellationRequested)
            {
                if (DateTime.UtcNow >= deadline) throw new IOException("Unable to activate the running Hitbench instance.", error);
                await Task.Delay(25, token);
            }
        }
    }

    public void Dispose()
    {
        try { channel?.Dispose(); }
        finally { channel = null; lease?.Dispose(); lease = null; }
    }
}
