using NAudio.Wave;
using osuHitsounder.Core.Entities;
using osuHitsounder.Services;

namespace osuHitsounder.Test.Services;

public class ScheduledPlaybackTests
{
    [Test]
    public async Task PlaybackWithoutUiRefreshIncludesTailOnSeekAndResumesConsumedFrames()
    {
        using var project = new TestProjectFolder("hit.wav");
        var players = new List<StubWavePlayer>();
        using var audio = new WorkstationAudioService(path => Stream(path.EndsWith("hit.wav") ? .4f : .2f),
            () => { var player = new StubWavePlayer(); players.Add(player); return player; });
        audio.Open("song.wav");
        await audio.UpdateSamplesAsync(project.ProjectPath,[new SamplePlaybackEvent(1,"hit.wav",10,100,100)],CancellationToken.None);
        audio.Play();
        var output = new float[4096];
        players[^1].Provider!.ToSampleProvider().Read(output,0,4096);
        Assert.That(output[880..886],Is.EqualTo(new[]{.2f,.2f,.6f,.6f,.6f,.6f}).Within(.0001));
        players[^1].Position = 1024 * 8;
        audio.Pause();
        audio.Play();
        Assert.That(players, Has.Count.EqualTo(2));
        Assert.That(audio.GetSnapshot().Position.TotalSeconds,Is.EqualTo(1024d/44100).Within(.000001));
        Assert.That(Read(players[^1]),Is.EqualTo(new[]{.6f,.6f}).Within(.0001));
        audio.Seek(TimeSpan.FromMilliseconds(20));
        Assert.That(Read(players[^1]),Is.EqualTo(new[]{.6f,.6f}).Within(.0001));
    }

    [Test]
    public async Task InvalidScheduleCannotReplaceLastSuccessfulSchedule()
    {
        using var project = new TestProjectFolder("hit.wav");
        var player = new StubWavePlayer();
        using var audio = new WorkstationAudioService(path=>Stream(path.EndsWith("hit.wav")?.4f:.2f),()=>player);
        audio.Open("song.wav");
        await audio.UpdateSamplesAsync(project.ProjectPath,[new SamplePlaybackEvent(1,"hit.wav",0,100,100)],CancellationToken.None);
        await audio.UpdateSamplesAsync(project.ProjectPath,[new SamplePlaybackEvent(2,"hit.wav",0,0,100),new SamplePlaybackEvent(2,"hit.wav",0,0,100)],CancellationToken.None);
        audio.Play();
        Assert.That(audio.GetSnapshot().Error, Does.Contain("prepare"));
        Assert.That(Read(player),Is.EqualTo(new[]{.6f,.6f}).Within(.0001));
    }

    internal static WaveStream Stream(float value)
    {
        var samples = Enumerable.Repeat(value,44100*2).ToArray();
        var bytes = new byte[samples.Length*4];
        Buffer.BlockCopy(samples,0,bytes,0,bytes.Length);
        return new RawSourceWaveStream(new MemoryStream(bytes),WaveFormat.CreateIeeeFloatWaveFormat(44100,2));
    }

    internal static float[] Read(StubWavePlayer player)
    {
        var output = new float[2];
        player.Provider!.ToSampleProvider().Read(output,0,2);
        return output;
    }
}
