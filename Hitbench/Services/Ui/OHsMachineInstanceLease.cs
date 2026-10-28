using System.Security.AccessControl;
using System.Security.Principal;

namespace Hitbench.Services.Ui;

public sealed class OHsMachineInstanceLease : IDisposable
{
    private readonly ManualResetEvent release = new(false);
    private readonly Thread thread;
    private int disposed;

    private OHsMachineInstanceLease(string identity, CancellationToken token, TaskCompletionSource<OHsMachineInstanceLease?> completion)
    {
        thread = new Thread(() => Own(identity, token, completion)) { IsBackground = true, Name = "Hitbench instance ownership" };
        thread.Start();
    }

    public static Task<OHsMachineInstanceLease?> TryAcquireAsync(string identity, CancellationToken token)
    {
        var completion = new TaskCompletionSource<OHsMachineInstanceLease?>(TaskCreationOptions.RunContinuationsAsynchronously);
        _ = new OHsMachineInstanceLease(identity, token, completion);
        return completion.Task;
    }

    private void Own(string identity, CancellationToken token, TaskCompletionSource<OHsMachineInstanceLease?> completion)
    {
        var acquired = false;
        try
        {
            token.ThrowIfCancellationRequested();
            var security = new MutexSecurity();
            security.AddAccessRule(new MutexAccessRule(WindowsIdentity.GetCurrent().User!, MutexRights.FullControl, AccessControlType.Allow));
            security.AddAccessRule(new MutexAccessRule(new SecurityIdentifier(WellKnownSidType.AuthenticatedUserSid, null), MutexRights.Synchronize | MutexRights.Modify, AccessControlType.Allow));
            Mutex mutex;
            try
            {
                mutex = MutexAcl.TryOpenExisting(identity, MutexRights.Synchronize | MutexRights.Modify, out var existing)
                    ? existing! : MutexAcl.Create(false, identity, out _, security);
            }
            catch (UnauthorizedAccessException)
            {
                // Another account can create the object between Open and Create.
                mutex = MutexAcl.OpenExisting(identity, MutexRights.Synchronize | MutexRights.Modify);
            }
            using var ownedMutex = mutex;
            try { acquired = mutex.WaitOne(0); }
            catch (AbandonedMutexException) { acquired = true; }
            if (!acquired) { completion.TrySetResult(null); return; }
            try
            {
                token.ThrowIfCancellationRequested();
                completion.TrySetResult(this);
                release.WaitOne();
            }
            finally { mutex.ReleaseMutex(); }
        }
        catch (OperationCanceledException) { completion.TrySetCanceled(token); }
        catch (Exception error) { completion.TrySetException(error); }
        finally { if (!completion.Task.IsCompletedSuccessfully || completion.Task.Result is null) release.Dispose(); }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0) return;
        release.Set();
        thread.Join();
        release.Dispose();
    }
}
