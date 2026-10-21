using System.IO;
using osuHitsounder.Services.FileSystem;

namespace osuHitsounder.Services;

public sealed class PlaybackVolumeSettings(IAppSettingsService? settings = null, int applicationVolume = 100)
{
    private int applicationVolume = Math.Clamp(applicationVolume, 0, 100);
    private Task saveTail = Task.CompletedTask;
    private Exception? saveError;
    private int? pendingVolume;
    private bool saving;
    public bool HasPendingSaves => !saveTail.IsCompleted || saveError is not null;

    private void QueueSave(int value)
    {
        pendingVolume = value;
        if (saving) return;
        saving = true;
        saveTail = SavePendingAsync();
    }

    private async Task SavePendingAsync()
    {
        try
        {
            while (pendingVolume is { } value)
            {
                pendingVolume = null;
                try { await settings!.SaveVolumeAsync(value, CancellationToken.None); saveError = null; }
                catch (Exception error) { saveError = error; }
            }
        }
        finally { saving = false; }
    }

    public async Task FlushAsync(CancellationToken cancellationToken = default)
    {
        Task pending;
        do { pending = saveTail; await pending.WaitAsync(cancellationToken); }
        while (!ReferenceEquals(pending, saveTail));
        if (saveError is null) return;
        QueueSave(ApplicationVolume);
        do { pending = saveTail; await pending.WaitAsync(cancellationToken); }
        while (!ReferenceEquals(pending, saveTail));
        if (saveError is not null) throw new IOException("Could not save application volume.", saveError);
    }
    private int samplesVolume = 100;

    public int ApplicationVolume
    {
        get => applicationVolume;
        set
        {
            var next = Math.Clamp(value, 0, 100);
            if (next == applicationVolume) return;
            applicationVolume = next;
            if (settings is not null) QueueSave(next);
        }
    }

    public int SamplesVolume
    {
        get => samplesVolume;
        set => samplesVolume = Math.Clamp(value, 0, 100);
    }
}
