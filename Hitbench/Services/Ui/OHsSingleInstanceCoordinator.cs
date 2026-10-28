using System.IO;
using System.Diagnostics;

namespace Hitbench.Services.Ui;

public sealed class OHsSingleInstanceCoordinator(string identity = "Hitbench.Application.Activation", Action<uint>? grantForeground = null, TimeSpan? handoffTimeout = null) : IDisposable
{
    public const string MachineIdentity = "Global\\Hitbench.Application.Instance";
    private OHsMachineInstanceLease? lease;
    private OHsInstanceActivationChannel? channel;

    public async Task<bool> RunPrimaryOrActivateAsync(Func<CancellationToken, Task> initializePrimary, Action requestActivation, CancellationToken token)
    {
        var started = Stopwatch.GetTimestamp();
        var timeout = handoffTimeout ?? TimeSpan.FromSeconds(5);
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
                var remaining = timeout - Stopwatch.GetElapsedTime(started);
                if (remaining <= TimeSpan.Zero) throw new IOException("Activation handoff timed out.");
                await client.RequestAsync(remaining < TimeSpan.FromMilliseconds(200) ? remaining : TimeSpan.FromMilliseconds(200), token);
                return false;
            }
            catch (Exception error) when (error is IOException || error is OperationCanceledException && !token.IsCancellationRequested)
            {
                if (Stopwatch.GetElapsedTime(started) >= timeout) throw new IOException("Unable to activate the running Hitbench instance.", error);
                await Task.Delay(25, token);
            }
        }
    }

    public void Dispose()
    {
        try { StopActivation(); }
        finally { lease?.Dispose(); lease = null; }
    }

    public void StopActivation() => Interlocked.Exchange(ref channel, null)?.Dispose();
}
