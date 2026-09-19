using NAudio.Wave;
using osuHitsounder.Services;

namespace osuHitsounder.Test.Services;

public class WorkstationAudioServiceTests
{
    [Test]
    public void PlayPauseResumeAndStopPreserveExpectedState()
    {
        var player = new StubWavePlayer();
        using var service = new WorkstationAudioService(_ => CreateStream(), () => player);
        service.Open("test.wav");
        service.Play();
        Assert.That(service.GetSnapshot().IsPlaying, Is.True);
        service.Pause();
        Assert.That(service.GetSnapshot().IsPlaying, Is.False);
        service.Play();
        Assert.That(service.GetSnapshot().IsPlaying, Is.True);
        service.Stop();
        Assert.Multiple(() =>
        {
            Assert.That(service.GetSnapshot().Position, Is.EqualTo(TimeSpan.Zero));
            Assert.That(service.GetSnapshot().IsPlaying, Is.False);
        });
    }

    [Test]
    public void NaturalEndAndPlaybackErrorResetPosition()
    {
        var player = new StubWavePlayer();
        using var service = new WorkstationAudioService(_ => CreateStream(), () => player);
        service.Open("test.wav");
        service.Play();
        player.RaiseStopped(new IOException("device lost"));
        var snapshot = service.GetSnapshot();
        Assert.That(snapshot.Position, Is.EqualTo(TimeSpan.Zero));
        Assert.That(snapshot.Error, Does.Contain("device lost"));
    }

    [Test]
    public void DeviceFailureDoesNotThrowAndDisposeIsIdempotent()
    {
        var player = new StubWavePlayer { FailInitialization = true };
        var service = new WorkstationAudioService(_ => CreateStream(), () => player);
        service.Open("test.wav");
        Assert.DoesNotThrow(service.Play);
        Assert.That(service.GetSnapshot().Error, Does.Contain("No device"));
        Assert.DoesNotThrow(service.Dispose);
        Assert.DoesNotThrow(service.Dispose);
        Assert.That(player.IsDisposed, Is.True);
    }

    private static WaveStream CreateStream()
    {
        var format = WaveFormat.CreateIeeeFloatWaveFormat(10, 1);
        return new RawSourceWaveStream(new MemoryStream(new byte[format.BlockAlign * 10]), format);
    }
}
