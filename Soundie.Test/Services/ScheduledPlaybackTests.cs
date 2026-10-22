using NAudio.Wave;
using Soundie.Core.Entities;
using Soundie.Services;

namespace Soundie.Test.Services;

public class ScheduledPlaybackTests
{
    [TestCase(442)]
    [TestCase(1764)]
    [TestCase(3528)]
    [TestCase(37)]
    public async Task OutputChunksPreserveStereoSequenceAndShortFinalBuffer(int chunkSamples)
    {
        using var project = new TestProjectFolder();
        var expected = Enumerable.Range(0, 10006).Select(i => (i % 997 - 498) / 1000f).ToArray();
        var bytes = new byte[expected.Length * sizeof(float)];
        Buffer.BlockCopy(expected, 0, bytes, 0, bytes.Length);
        var player = new StubWavePlayer();
        using var service = new WorkstationAudioService(_ => new RawSourceWaveStream(
            new MemoryStream(bytes), WaveFormat.CreateIeeeFloatWaveFormat(44100, 2)), () => player);
        service.Open("song.wav");
        await service.UpdateSamplesAsync(project.ProjectPath, [], CancellationToken.None);
        service.Play();
        var provider = player.Provider!.ToSampleProvider();
        var actual = new List<float>();
        var buffer = new float[chunkSamples + 4];
        int read;
        do
        {
            Array.Fill(buffer, -9f);
            read = provider.Read(buffer, 2, chunkSamples);
            Assert.That(buffer.Take(2), Is.All.EqualTo(-9f));
            Assert.That(buffer.Skip(2 + read), Is.All.EqualTo(-9f));
            actual.AddRange(buffer.AsSpan(2, read).ToArray());
            Assert.That(actual.Count, Is.LessThanOrEqualTo(expected.Length));
        } while (read > 0);
        Assert.That(actual, Is.EqualTo(expected));
        player.Position = bytes.Length;
        Assert.That(service.GetSnapshot().IsPlaying, Is.True, "EOF must leave the device time to drain.");
        Assert.That(service.GetSnapshot().Position.TotalSeconds, Is.EqualTo(5003d / 44100).Within(.000001));
        player.RaiseStopped();
        Assert.That(service.GetSnapshot().IsPlaying, Is.False);
        Assert.That(provider.Read(buffer, 2, chunkSamples), Is.Zero);
    }

    [TestCase("cancel")]
    [TestCase("open")]
    [TestCase("dispose")]
    public async Task RetiredPreparationCannotPublishAfterLifecycleChange(string change)
    {
        using var project = new TestProjectFolder("hit.wav", "slow.wav");
        using var release = new ManualResetEventSlim();
        using var cancellation = new CancellationTokenSource();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var player = new StubWavePlayer();
        using var audio = new WorkstationAudioService(path=>
        {
            if(path.EndsWith("slow.wav"))
            {
                entered.TrySetResult();
                if(!release.Wait(TimeSpan.FromSeconds(5))) throw new TimeoutException("Decoder release missing");
                return Stream(.8f);
            }
            return Stream(path.EndsWith("hit.wav") ? .4f : .2f);
        },()=>player);
        audio.Open("song.wav");
        await audio.UpdateSamplesAsync(project.ProjectPath, [new SamplePlaybackEvent(1, "hit.wav", 0, 100)], CancellationToken.None);
        var updating = audio.UpdateSamplesAsync(project.ProjectPath, [new SamplePlaybackEvent(2, "slow.wav", 0, 100)], cancellation.Token);
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            if(change=="cancel") cancellation.Cancel();
            else if(change=="open") audio.Open("other.wav");
            else audio.Dispose();
            release.Set();
            try { await updating; }
            catch(OperationCanceledException) when(change=="cancel") { }
            if(change=="dispose")
            {
                Assert.That(audio.GetSnapshot().IsPlaying,Is.False);
                Assert.Throws<ObjectDisposedException>(audio.Play);
            }
            else
            {
                if(change=="open") await audio.UpdateSamplesAsync(project.ProjectPath,[],CancellationToken.None);
                audio.Play();
                Assert.That(Read(player),Is.EqualTo(change=="cancel" ? new[]{.6f,.6f}:new[]{.2f,.2f}).Within(.0001));
            }
        }
        finally { release.Set(); try { await updating; } catch(OperationCanceledException) { } }
    }

    [Test]
    public async Task TriggeredPlaybackIncludesTailOnSeekAndResumesConsumedFrames()
    {
        using var project = new TestProjectFolder("hit.wav");
        var players = new List<StubWavePlayer>();
        using var audio = new WorkstationAudioService(path => Stream(path.EndsWith("hit.wav") ? .4f : .2f),
            () => { var player = new StubWavePlayer(); players.Add(player); return player; });
        audio.Open("song.wav");
        await audio.UpdateSamplesAsync(project.ProjectPath, [new SamplePlaybackEvent(1, "hit.wav", 10, 100)], CancellationToken.None);
        audio.Play();
        var output = new float[4096];
        players[^1].Provider!.ToSampleProvider().Read(output,0,882);
        players[^1].Provider!.ToSampleProvider().Read(output,882,4096-882);
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
        await audio.UpdateSamplesAsync(project.ProjectPath, [new SamplePlaybackEvent(1, "hit.wav", 0, 100)], CancellationToken.None);
        await audio.UpdateSamplesAsync(project.ProjectPath, [new SamplePlaybackEvent(2, "hit.wav", 0, 100), new SamplePlaybackEvent(2, "hit.wav", 0, 100)], CancellationToken.None);
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
