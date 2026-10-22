using NAudio.Wave;
using Soundie.Services;

namespace Soundie.Test.Services;

public class PlaybackOutputLifecycleTests
{
    [Test]
    public void CompatibilityOutputUsesStableQueueWithoutOpeningDevice()
    {
        using var player = WorkstationAudioService.CreateOutput();
        Assert.That(player, Is.TypeOf<WaveOutEvent>());
        var wave = (WaveOutEvent)player;
        Assert.That(wave.DesiredLatency, Is.EqualTo(60));
        Assert.That(wave.NumberOfBuffers, Is.EqualTo(3));
    }

    [Test]
    public async Task CompletedOutputRetainsEndpointUntilExplicitRestart()
    {
        var player = new StubWavePlayer();
        var created = 0;
        using var project = new TestProjectFolder();
        using var service = new WorkstationAudioService(_ => new RawSourceWaveStream(
            new MemoryStream(new byte[8000]), WaveFormat.CreateIeeeFloatWaveFormat(1000, 2)), () => { created++; return player; });
        service.Open("song.wav");
        await service.UpdateSamplesAsync(project.ProjectPath, [], CancellationToken.None);
        service.Play();
        player.RaiseStopped();
        Assert.That(service.GetSnapshot().Position.TotalSeconds, Is.EqualTo(1).Within(.001));
        Assert.That(created, Is.EqualTo(1));
        service.Stop();
        Assert.That(service.GetSnapshot().Position, Is.EqualTo(TimeSpan.Zero));
    }
}
