using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using Microsoft.Win32.SafeHandles;

namespace Hitbench.Services.Ui;

public sealed class OHsInstanceActivationChannel(string identity, Action<uint>? grantForeground = null) : IDisposable
{
    private readonly CancellationTokenSource lifetime = new();
    private Task listener = Task.CompletedTask;
    private int disposed;

    public void Start(Action requestActivation)
    {
        var server = CreateServer();
        listener = ListenAsync(server, requestActivation);
    }

    private NamedPipeServerStream CreateServer()
    {
        using var currentIdentity = WindowsIdentity.GetCurrent();
        var security = new PipeSecurity();
        security.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.NetworkSid, null), PipeAccessRights.FullControl, AccessControlType.Deny));
        security.AddAccessRule(new PipeAccessRule(currentIdentity.User!, PipeAccessRights.FullControl, AccessControlType.Allow));
        security.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.AuthenticatedUserSid, null), PipeAccessRights.ReadData | PipeAccessRights.WriteData | PipeAccessRights.Synchronize, AccessControlType.Allow));
        return NamedPipeServerStreamAcl.Create(identity, PipeDirection.InOut, 1, PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous | PipeOptions.FirstPipeInstance, 64, 64, security);
    }

    private async Task ListenAsync(NamedPipeServerStream server, Action requestActivation)
    {
        using (server)
        {
            while (!lifetime.IsCancellationRequested)
            {
                try
                {
                    await server.WaitForConnectionAsync(lifetime.Token).ConfigureAwait(false);
                    using var deadline = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
                    deadline.CancelAfter(TimeSpan.FromSeconds(2));
                    var hello = new byte[5];
                    hello[0] = 1;
                    BitConverter.TryWriteBytes(hello.AsSpan(1), Environment.ProcessId);
                    await server.WriteAsync(hello, deadline.Token).ConfigureAwait(false);
                    var request = new byte[1];
                    await server.ReadExactlyAsync(request, deadline.Token).ConfigureAwait(false);
                    if (request[0] == 1)
                    {
                        requestActivation();
                        await server.WriteAsync(new byte[] { 1 }, deadline.Token).ConfigureAwait(false);
                    }
                }
                catch (OperationCanceledException) { }
                catch (IOException error) { Trace.TraceWarning("Activation channel: {0}", error); }
                catch (Exception error) { Trace.TraceError("Activation callback: {0}", error); }
                finally
                {
                    try { if (server.IsConnected) server.Disconnect(); }
                    catch (IOException error) { Trace.TraceWarning("Activation disconnect: {0}", error); }
                }
            }
        }
    }

    public async Task RequestAsync(TimeSpan timeout, CancellationToken token)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(timeout);
        using var client = new NamedPipeClientStream(".", identity, PipeAccessRights.ReadData | PipeAccessRights.WriteData | PipeAccessRights.Synchronize,
            PipeOptions.Asynchronous, TokenImpersonationLevel.Anonymous, HandleInheritability.None);
        await client.ConnectAsync(deadline.Token).ConfigureAwait(false);
        var hello = new byte[5];
        await client.ReadExactlyAsync(hello, deadline.Token).ConfigureAwait(false);
        var pid = BitConverter.ToUInt32(hello, 1);
        if (hello[0] != 1 || !GetNamedPipeServerProcessId(client.SafePipeHandle, out var actualPid) || pid != actualPid)
            throw new IOException("Invalid activation endpoint.");
        (grantForeground ?? (processId => AllowSetForegroundWindow(processId)))(pid);
        await client.WriteAsync(new byte[] { 1 }, deadline.Token).ConfigureAwait(false);
        var ack = new byte[1];
        await client.ReadExactlyAsync(ack, deadline.Token).ConfigureAwait(false);
        if (ack[0] != 1) throw new IOException("Invalid activation acknowledgement.");
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0) return;
        lifetime.Cancel();
        listener.GetAwaiter().GetResult();
        lifetime.Dispose();
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetNamedPipeServerProcessId(SafePipeHandle pipe, out uint processId);
    [DllImport("user32.dll")]
    private static extern bool AllowSetForegroundWindow(uint processId);
}
