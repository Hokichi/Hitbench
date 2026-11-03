using Hitbench.Resources.DataModels;
using Hitbench.Services.FileSystem;

namespace Hitbench.Services.Ui;

public sealed class OHsUpdateRestartService(OHsUpdateHandoff handoff,
    Func<CancellationToken, Task<bool>> prepareClose,
    Func<Action, CancellationToken, Task<bool>> requestClose, Action resume) : IOHsUpdateRestartService
{
    private bool running;
    public async Task<bool> TryRestartAsync(OHsStagedUpdate update, CancellationToken token)
    {
        if (running) return false;
        running = true;
        string? job = null;
        var committed = false;
        try
        {
            job = await handoff.PrepareAsync(update, token);
            if (!await prepareClose(token)) return false;
            token.ThrowIfCancellationRequested();
            await requestClose(() => { handoff.Commit(job); committed = true; }, token);
            return committed;
        }
        finally
        {
            if (!committed)
            {
                try { if (job is not null) handoff.Abort(job); }
                finally { resume(); }
            }
            running = false;
        }
    }
}
