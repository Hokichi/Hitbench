using osuHitsounder.Services;

namespace osuHitsounder.Test.Services;

public class AudioOutputDeviceTests
{
    [Test]
    public void PlayedCursorExcludesOriginAndNeverMovesBackward()
    {
        var player = new StubWavePlayer { Position = 800 };
        using var output = new AudioOutputDevice(() => player);
        output.Init(new ScheduledSampleMixer(DecodedAudio.CopyFrom(new float[20000]),0));
        output.Play();
        player.Position = 800 + 1024 * 8;
        Assert.That(output.GetPlayedFrames(), Is.EqualTo(1024));
        player.Position = 0;
        Assert.That(output.GetPlayedFrames(), Is.EqualTo(1024));
    }

    [Test]
    public void DeviceCallbacksWaitForControlPollAndRetirementDetachesThem()
    {
        var player = new StubWavePlayer();
        using var output = new AudioOutputDevice(() => player);
        output.Init(new ScheduledSampleMixer(DecodedAudio.CopyFrom(new float[20]),0));
        var notifications = 0;
        output.PlaybackStopped += (_,_) => notifications++;
        output.Play();
        player.RaiseStopped(new InvalidOperationException("lost"));
        Assert.That(notifications, Is.Zero);
        output.Poll();
        Assert.That(notifications, Is.EqualTo(1));
        output.Dispose();
        player.RaiseRetiredStopped();
        output.Poll();
        Assert.That(notifications, Is.EqualTo(1));
        Assert.That(player.IsDisposed, Is.True);
    }

    [Test]
    public void FailedInitializationDisposesPartialPlayer()
    {
        var player = new StubWavePlayer { FailInitialization = true };
        using var output = new AudioOutputDevice(() => player);
        Assert.Throws<InvalidOperationException>(() => output.Init(new ScheduledSampleMixer(DecodedAudio.CopyFrom([0,0]),0)));
        Assert.That(player.IsDisposed, Is.True);
        Assert.That(player.HasPlaybackStoppedSubscribers, Is.False);
    }
}
