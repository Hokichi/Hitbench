using NAudio.Wave;
using Onset.Services;

namespace Onset.Test.Services;

public class BufferedSampleTimingTests
{
    [TestCase(10, 37), TestCase(10, 512), TestCase(10, 1024)]
    [TestCase(60, 37), TestCase(60, 512), TestCase(60, 1024)]
    [TestCase(120, 37), TestCase(120, 512), TestCase(120, 1024)]
    public async Task RenderAheadDoesNotShiftSampleAttack(int aheadMilliseconds, int chunkFrames)
    {
        using var project = new TestProjectFolder("hit.wav");
        var player = new StubWavePlayer();
        using var audio = new WorkstationAudioService(path =>
        {
            var signal = path.EndsWith("hit.wav") ? new float[882] : new float[44100];
            signal[path.EndsWith("hit.wav") ? 1 : 8820] = .5f;
            return Stream(signal);
        }, () => player);
        using var wake = new SemaphoreSlim(0);
        using var waiting = new SemaphoreSlim(0);
        await using var coordinator = new MarkerPlaybackCoordinator(audio, async (_, token) =>
        {
            waiting.Release();
            await wake.WaitAsync(token);
        });
        audio.Open("song.wav");
        var events = new[] { new Onset.Core.Entities.SamplePlaybackEvent(1, "hit.wav", 100, 100) };
        await coordinator.ConfigureAsync(new PlaybackMarkerClock([0], [120]), TimeSpan.FromSeconds(.5), default);
        await coordinator.UpdateSamplesAsync(project.ProjectPath, events, default);
        await coordinator.PlayAsync(default);
        await waiting.WaitAsync().WaitAsync(TimeSpan.FromSeconds(3));
        var pcm = new List<float>();
        var buffer = new float[chunkFrames * 2];
        var provider = player.Provider!.ToSampleProvider();
        int count;
        while ((count = provider.Read(buffer, 0, buffer.Length)) > 0)
        {
            pcm.AddRange(buffer.AsSpan(0, count).ToArray());
            player.Position = Math.Max(0, pcm.Count / 2 - aheadMilliseconds * 441 / 10) * 8L;
            wake.Release();
            await waiting.WaitAsync().WaitAsync(TimeSpan.FromSeconds(3));
        }
        await coordinator.PauseAsync(default);
        Assert.Multiple(() =>
        {
            Assert.That(pcm[8820], Is.EqualTo(.5f), "Song impulse at its stored timestamp");
            Assert.That(pcm[8821], Is.EqualTo(.5f), "Sample attack must share the song frame despite render-ahead");
            Assert.That(pcm.Where((_, i) => i % 2 == 1).Count(value => value != 0), Is.EqualTo(1));
        });
    }

    [Test]
    public async Task PauseBeforeBufferedAttackPreservesContinuousSlice()
    {
        using var project = new TestProjectFolder("hit.wav");
        var players = new List<StubWavePlayer>();
        using var audio = new WorkstationAudioService(path => Stream(path.EndsWith("hit.wav")
            ? [.5f, .6f, .3f, .4f] : new float[44100]),
            () => { var player = new StubWavePlayer(); players.Add(player); return player; });
        audio.Open("song.wav");
        await audio.UpdateSamplesAsync(project.ProjectPath, [new(1, "hit.wav", 100, 100)], default);
        audio.Play();
        var buffered = new float[12000];
        players[^1].Provider!.ToSampleProvider().Read(buffered, 0, buffered.Length);
        Assert.That(buffered[8820..8824], Is.EqualTo(new[] { .5f, .6f, .3f, .4f }));
        players[^1].Position = 2205 * 8;
        audio.Pause();
        audio.Play();
        var expected = new float[39690];
        new[] { .5f, .6f, .3f, .4f }.CopyTo(expected, 4410);
        Assert.That(ReadAll(players[^1]), Is.EqualTo(expected));
    }

    [TestCase(50), TestCase(100), TestCase(150)]
    public async Task SeekMatchesContinuousRenderSlice(int seekMilliseconds)
    {
        using var project = new TestProjectFolder("hit.wav");
        var players = new List<StubWavePlayer>();
        var hit = Enumerable.Range(0, 8820).Select(i => (i / 2 + 1) / 10000f).ToArray();
        using var audio = new WorkstationAudioService(path => Stream(path.EndsWith("hit.wav") ? hit : new float[44100]),
            () => { var player = new StubWavePlayer(); players.Add(player); return player; });
        audio.Open("song.wav");
        await audio.UpdateSamplesAsync(project.ProjectPath, [new(1, "hit.wav", 100, 100)], default);
        audio.Play();
        var expected = new float[44100];
        hit.CopyTo(expected, 8820);
        Assert.That(ReadAll(players[^1]), Is.EqualTo(expected));
        audio.Seek(TimeSpan.FromMilliseconds(seekMilliseconds));
        Assert.That(ReadAll(players[^1]), Is.EqualTo(expected[(seekMilliseconds * 882 / 10)..]));
        audio.Stop();
        audio.Play();
        Assert.That(ReadAll(players[^1]), Is.EqualTo(expected));
    }

    [TestCase(0, false), TestCase(2, false), TestCase(4, false), TestCase(2, true)]
    public async Task LatePreparationUsesCurrentSourceOffset(int frames, bool pause)
    {
        using var project = new TestProjectFolder("slow.wav");
        using var release = new ManualResetEventSlim();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var players = new List<StubWavePlayer>();
        using var audio = new WorkstationAudioService(path =>
        {
            if (!path.EndsWith("slow.wav")) return Stream(new float[40]);
            entered.TrySetResult();
            if (!release.Wait(TimeSpan.FromSeconds(5))) throw new TimeoutException("Decoder release missing");
            return Stream([.1f, .1f, .2f, .2f, .3f, .3f, .4f, .4f]);
        }, () => { var player = new StubWavePlayer(); players.Add(player); return player; });
        audio.Open("song.wav");
        await audio.UpdateSamplesAsync(project.ProjectPath, [], default);
        audio.Play();
        var updating = audio.UpdateSamplesAsync(project.ProjectPath, [new(1, "slow.wav", 0, 100)], default);
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
            if (frames > 0) players[^1].Provider!.ToSampleProvider().Read(new float[frames * 2], 0, frames * 2);
            players[^1].Position = frames * 8;
            if (pause) audio.Pause();
            release.Set();
            await updating;
            if (pause) audio.Play();
            var output = new float[8];
            players[^1].Provider!.ToSampleProvider().Read(output, 0, output.Length);
            float[] expected = frames switch
            {
                0 => [.1f, .1f, .2f, .2f, .3f, .3f, .4f, .4f],
                2 => [.3f, .3f, .4f, .4f, 0f, 0f, 0f, 0f],
                _ => new float[8]
            };
            Assert.That(output, Is.EqualTo(expected));
        }
        finally { release.Set(); await updating; }
    }

    [Test]
    public async Task RemovingDuringDecodeSuppressesUnrenderedTail()
    {
        using var project = new TestProjectFolder("hit.wav", "slow.wav");
        using var release = new ManualResetEventSlim();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var player = new StubWavePlayer();
        using var audio = new WorkstationAudioService(path =>
        {
            if (path.EndsWith("slow.wav"))
            {
                entered.TrySetResult();
                if (!release.Wait(TimeSpan.FromSeconds(5))) throw new TimeoutException("Decoder release missing");
                return Stream([.8f, .8f]);
            }
            return Stream(path.EndsWith("hit.wav") ? Enumerable.Repeat(.4f, 44100).ToArray() : new float[44100]);
        }, () => player);
        audio.Open("song.wav");
        await audio.UpdateSamplesAsync(project.ProjectPath, [new(1, "hit.wav", 0, 100)], default);
        audio.Play();
        var buffered = new float[2];
        player.Provider!.ToSampleProvider().Read(buffered, 0, 2);
        var updating = audio.UpdateSamplesAsync(project.ProjectPath, [new(2, "slow.wav", 100, 100)], default);
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
            var next = new float[2];
            player.Provider!.ToSampleProvider().Read(next, 0, 2);
            Assert.That(next, Is.All.Zero);
            Assert.That(buffered, Is.EqualTo(new[] { .4f, .4f }));
            release.Set();
            await updating;
            var expected = new float[44096];
            expected[8816] = expected[8817] = .8f;
            Assert.That(ReadAll(player), Is.EqualTo(expected));
        }
        finally { release.Set(); await updating; }
    }

    [TestCase(false), TestCase(true)]
    public async Task AbsentOrBlockedMarkerDoesNotChangePcm(bool startWorker)
    {
        using var project = new TestProjectFolder("hit.wav");
        var player = new StubWavePlayer();
        using var audio = new WorkstationAudioService(path => Stream(path.EndsWith("hit.wav") ? [.5f, .6f] : new float[44100]), () => player);
        await using var coordinator = new MarkerPlaybackCoordinator(audio, (_, token) => Task.Delay(Timeout.Infinite, token));
        audio.Open("song.wav");
        await audio.UpdateSamplesAsync(project.ProjectPath, [new(1, "hit.wav", 100, 100)], default);
        if (startWorker)
        {
            await coordinator.ConfigureAsync(new PlaybackMarkerClock([0], [120]), TimeSpan.FromSeconds(.5), default);
            await coordinator.PlayAsync(default);
        }
        else audio.Play();
        var expected = new float[44100];
        expected[8820] = .5f;
        expected[8821] = .6f;
        Assert.That(ReadAll(player), Is.EqualTo(expected));
        Assert.That(coordinator.Latest.Playback.Position, Is.EqualTo(TimeSpan.Zero));
    }

    private static float[] ReadAll(StubWavePlayer player)
    {
        var result = new List<float>();
        var buffer = new float[733];
        var provider = player.Provider!.ToSampleProvider();
        int count;
        while ((count = provider.Read(buffer, 0, buffer.Length)) > 0)
            result.AddRange(buffer.AsSpan(0, count).ToArray());
        return result.ToArray();
    }

    internal static WaveStream Stream(float[] samples)
    {
        var bytes = new byte[samples.Length * sizeof(float)];
        Buffer.BlockCopy(samples, 0, bytes, 0, bytes.Length);
        return new RawSourceWaveStream(new MemoryStream(bytes), WaveFormat.CreateIeeeFloatWaveFormat(44100, 2));
    }
}
