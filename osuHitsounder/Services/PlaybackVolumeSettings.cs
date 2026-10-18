using System.IO;
using osuHitsounder.Services.FileSystem;

namespace osuHitsounder.Services;

public sealed class PlaybackVolumeSettings(IAppSettingsService? settings = null, int applicationVolume = 100)
{
    private int applicationVolume = Math.Clamp(applicationVolume, 0, 100);
    private Task saveTail = Task.CompletedTask;
    private Exception? saveError;
    public bool HasPendingSaves => !saveTail.IsCompleted || saveError is not null;

    private async Task SaveAsync(Task previous, int value)
    {
        await previous;
        try { await settings!.SaveVolumeAsync(value, CancellationToken.None); saveError = null; }
        catch (Exception error) { saveError = error; }
    }

    public async Task FlushAsync(CancellationToken cancellationToken = default)
    {
        Task pending;
        do { pending = saveTail; await pending.WaitAsync(cancellationToken); }
        while (!ReferenceEquals(pending, saveTail));
        if (saveError is null) return;
        saveTail = SaveAsync(saveTail, ApplicationVolume);
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
            if (settings is not null) saveTail = SaveAsync(saveTail, next);
        }
    }

    public int SamplesVolume
    {
        get => samplesVolume;
        set => samplesVolume = Math.Clamp(value, 0, 100);
    }
}
