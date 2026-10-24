using Hitbench.Services;
using Hitbench.Test.Presentation;

namespace Hitbench.Test.Services;

public sealed class PlaybackVolumeSettingsTests
{
    [Test]
    public async Task RapidChangesFlushLatestValue()
    {
        var saved = new List<int>();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var settings = new StubAppSettingsService { SaveVolume = async (value, _) => { await release.Task; saved.Add(value); } };
        var volumes = new PlaybackVolumeSettings(settings, 27);
        Assert.That(volumes.ApplicationVolume, Is.EqualTo(27));
        Assert.That(saved, Is.Empty);
        volumes.ApplicationVolume = 30;
        for (var value = 31; value < 64; value++) volumes.ApplicationVolume = value;
        volumes.ApplicationVolume = 64;
        Assert.That(volumes.HasPendingSaves, Is.True);
        release.SetResult();
        await volumes.FlushAsync();
        Assert.That(saved, Is.EqualTo(new[] { 30, 64 }));
        Assert.That(volumes.HasPendingSaves, Is.False);
    }

    [Test]
    public async Task FailedLatestValueCanRetryWithoutAnotherEdit()
    {
        var failing = true;
        var settings = new StubAppSettingsService { SaveVolume = (_, _) => failing ? Task.FromException(new IOException("Failed")) : Task.CompletedTask };
        var volumes = new PlaybackVolumeSettings(settings);
        volumes.ApplicationVolume = 31;
        Assert.ThrowsAsync<IOException>(() => volumes.FlushAsync());
        Assert.That(volumes.HasPendingSaves, Is.True);
        failing = false;
        await volumes.FlushAsync();
        Assert.That(volumes.HasPendingSaves, Is.False);
        Assert.That(volumes.ApplicationVolume, Is.EqualTo(31));
    }
}
