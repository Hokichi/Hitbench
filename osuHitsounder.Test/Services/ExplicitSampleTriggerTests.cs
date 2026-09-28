using NAudio.Wave;
using osuHitsounder.Services;

namespace osuHitsounder.Test.Services;

public class ExplicitSampleTriggerTests
{
    [Test]
    public void ReadingAudioCannotStartPreparedSample()
    {
        var mixer = new TriggeredSampleMixer(new float[20], WaveFormat.CreateIeeeFloatWaveFormat(1000, 2), 0);
        mixer.ReplaceSamples([new PreparedSampleEvent(1, new float[] { .4f, .4f }, 1)]);
        var output = new float[4];
        mixer.Read(output, 0, 4);
        Assert.That(output, Is.All.Zero);
        mixer.TriggerSamples([99, 1]);
        mixer.Read(output, 0, 4);
        Assert.That(output, Is.EqualTo(new[] { .4f, .4f, 0f, 0f }));
    }

    [Test]
    public void OutputUsesSmallQueueWithoutOpeningDevice()
    {
        using var player = WorkstationAudioService.CreateOutput();
        Assert.That(player, Is.TypeOf<WaveOutEvent>());
        var wave = (WaveOutEvent)player;
        Assert.That(wave.DesiredLatency, Is.EqualTo(10));
        Assert.That(wave.NumberOfBuffers, Is.EqualTo(2));
    }

    [Test]
    public async Task CompletedOutputRetainsEndpointAndTriggerDoesNotRestartIt()
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
        service.TriggerSamples([1]);
        Assert.That(created, Is.EqualTo(1));
        service.Stop();
        Assert.That(service.GetSnapshot().Position, Is.EqualTo(TimeSpan.Zero));
    }
}
