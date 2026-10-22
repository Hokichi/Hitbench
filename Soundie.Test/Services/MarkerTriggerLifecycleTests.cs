using NAudio.Wave;
using Soundie.Core.Entities;
using Soundie.Services;

namespace Soundie.Test.Services;

public class MarkerTriggerLifecycleTests
{
    [Test]
    public async Task PausePreservesRenderedButUnheardTailAndDoesNotRetrigger()
    {
        using var project = new TestProjectFolder("hit.wav");
        var players = new List<StubWavePlayer>();
        using var audio = new WorkstationAudioService(path => path.EndsWith("hit.wav")
            ? Stream([.1f,.1f,.2f,.2f,.3f,.3f,.4f,.4f]) : Stream(new float[40]),
            () => { var player = new StubWavePlayer(); players.Add(player); return player; });
        audio.Open("song.wav");
        await audio.UpdateSamplesAsync(project.ProjectPath, [new(1, "hit.wav", 0, 100)], CancellationToken.None);
        audio.Play();
        Read(players[^1], 16);
        players[^1].Position = 2 * 8;
        audio.Pause();
        audio.Play();
        Assert.That(Read(players[^1], 6), Is.EqualTo(new[] { .3f,.3f,.4f,.4f,0f,0f }));
    }

    [Test]
    public async Task SeekRestoresTailAndExactTargetStartsOnlyOnce()
    {
        using var project = new TestProjectFolder("hit.wav");
        var players = new List<StubWavePlayer>();
        using var audio = new WorkstationAudioService(path => path.EndsWith("hit.wav")
            ? Stream(Enumerable.Repeat(.25f, 882).ToArray()) : Stream(new float[4410]),
            () => { var player = new StubWavePlayer(); players.Add(player); return player; });
        audio.Open("song.wav");
        await audio.UpdateSamplesAsync(project.ProjectPath, [new(1, "hit.wav", 10, 100)], CancellationToken.None);
        audio.Seek(TimeSpan.FromMilliseconds(15));
        audio.Play();
        Assert.That(Read(players[^1], 2), Is.All.EqualTo(.25f));
        audio.Seek(TimeSpan.FromMilliseconds(10));
        Assert.That(Read(players[^1], 2), Is.All.EqualTo(.25f));
        audio.Stop();
        audio.Play();
        Assert.That(Read(players[^1], 2), Is.All.Zero);
    }

    [TestCase("publish")]
    [TestCase("stop")]
    [TestCase("seek")]
    [TestCase("remove")]
    [TestCase("cancel")]
    [TestCase("failure")]
    [TestCase("device")]
    [TestCase("pause")]
    public async Task PreparedScheduleFollowsTransportAndCancellation(string action)
    {
        using var project = new TestProjectFolder("slow.wav");
        using var release = new ManualResetEventSlim();
        using var cancellation = new CancellationTokenSource();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var players = new List<StubWavePlayer>();
        using var audio = new WorkstationAudioService(path =>
        {
            if (!path.EndsWith("slow.wav")) return Stream(new float[44100]);
            entered.TrySetResult();
            if (!release.Wait(TimeSpan.FromSeconds(5))) throw new TimeoutException();
            if (action == "failure") throw new IOException("decode failed");
            return Stream([.5f,.6f,.3f,.4f]);
        }, () => { var p = new StubWavePlayer(); players.Add(p); return p; });
        audio.Open("song.wav");
        await audio.UpdateSamplesAsync(project.ProjectPath, [], CancellationToken.None);
        audio.Play();
        var update = audio.UpdateSamplesAsync(project.ProjectPath, [new(1, "slow.wav", 100, 100)], cancellation.Token);
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Task? removed = null;
            if (action == "stop") { audio.Stop(); audio.Play(); }
            if (action == "seek") audio.Seek(TimeSpan.Zero);
            if (action == "remove") removed = audio.UpdateSamplesAsync(project.ProjectPath, [], CancellationToken.None);
            if (action == "cancel") cancellation.Cancel();
            if (action == "pause") audio.Pause();
            if (action == "device") { players[^1].RaiseStopped(new IOException("lost")); audio.GetSnapshot(); audio.Play(); }
            release.Set();
            try { await update; } catch (OperationCanceledException) when (action == "cancel") { }
            if (removed is not null) await removed;
            if (action == "pause") audio.Play();
            var output = Read(players[^1], 9000);
            var expected = new float[9000];
            if (action is not ("remove" or "cancel" or "failure"))
                new[] { .5f, .6f, .3f, .4f }.CopyTo(expected, 8820);
            Assert.That(output, Is.EqualTo(expected));
            if (action == "failure") Assert.That(audio.GetSnapshot().Error, Does.Contain("decode failed"));
        }
        finally { release.Set(); try { await update; } catch (OperationCanceledException) { } }
    }

    private static WaveStream Stream(float[] samples)
    {
        var bytes = new byte[samples.Length * sizeof(float)];
        Buffer.BlockCopy(samples, 0, bytes, 0, bytes.Length);
        return new RawSourceWaveStream(new MemoryStream(bytes), WaveFormat.CreateIeeeFloatWaveFormat(44100, 2));
    }

    private static float[] Read(StubWavePlayer player, int count)
    {
        var output = new float[count];
        Assert.That(player.Provider!.ToSampleProvider().Read(output, 0, count), Is.EqualTo(count));
        return output;
    }
}
