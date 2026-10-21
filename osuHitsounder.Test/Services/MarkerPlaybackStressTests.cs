using System.Diagnostics;
using NAudio.Wave;
using osuHitsounder.Services;

namespace osuHitsounder.Test.Services;

public class MarkerPlaybackStressTests
{
    [TestCase(64), TestCase(128)]
    public async Task FullSongRendersEveryPlacementAtItsTimestamp(int tracks)
    {
        using var project = new TestProjectFolder("hit.wav");
        var events = MarkerStressFixture.Create(tracks);
        var player = new StubWavePlayer();
        using var audio = new WorkstationAudioService(path => Signal(path.EndsWith("hit.wav"), 302), () => player);
        audio.Open("song.wav");
        await audio.UpdateSamplesAsync(project.ProjectPath, events, default);
        audio.Play();
        // Independent PCM oracle: add a 44-frame rectangular hit at every stored millisecond timestamp.
        var expected = new float[44100 * 2 * 302];
        foreach (var item in events)
        {
            var offset = (int)decimal.Round(item.Timestamp * 441m / 10m, 0, MidpointRounding.AwayFromZero) * 2;
            for (var i = offset; i < offset + 88; i++) expected[i] += .001f;
        }
        for (var i = 0; i < expected.Length; i++) expected[i] = Math.Clamp(expected[i], -1, 1);
        var output = new float[1500];
        var provider = player.Provider!.ToSampleProvider();
        var position = 0;
        int read;
        while ((read = provider.Read(output, 0, output.Length)) > 0)
        {
            Assert.That(output.AsSpan(0, read).SequenceEqual(expected.AsSpan(position, read)), Is.True, $"PCM at float {position}");
            position += read;
        }
        Assert.That(position, Is.EqualTo(expected.Length));
    }

    [TestCase(8, 0), TestCase(8, 150), TestCase(8, 290)]
    [TestCase(64, 0), TestCase(64, 150), TestCase(64, 290)]
    [TestCase(128, 0), TestCase(128, 150), TestCase(128, 290), Explicit]
    public void MeasureMixerBaseline(int tracks, int seconds)
    {
        var events = MarkerStressFixture.Create(tracks);
        var hit = DecodedAudio.CopyFrom(new float[88]);
        var song = DecodedAudio.CopyFrom(new float[44100 * 2 * 302]);
        var schedule = new PreparedPlaybackSchedule(events.Select(e => new PreparedSampleEvent(e.Id, hit, 1,
            (long)decimal.Round(e.Timestamp * 441m / 10m, 0, MidpointRounding.AwayFromZero))).ToArray());
        var mixer = new ScheduledSampleMixer(song, seconds * 44100L);
        mixer.Publish(schedule);
        var output = new float[128];
        for (var i = 0; i < 100; i++) mixer.Read(output, 0, output.Length);
        var elapsed = new double[1000];
        var allocated = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < elapsed.Length; i++)
        {
            var start = Stopwatch.GetTimestamp();
            mixer.Read(output, 0, output.Length);
            elapsed[i] = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        }
        allocated = GC.GetAllocatedBytesForCurrentThread() - allocated;
        Array.Sort(elapsed);
        var range = schedule.GetOverlappingRange(seconds * 44100L, seconds * 44100L + 64);
        TestContext.Out.WriteLine($"tracks={tracks}; seconds={seconds}; placements={events.Length}; candidates={range.End-range.Start}; p95={elapsed[950]:F4}ms; p99={elapsed[990]:F4}ms; max={elapsed[^1]:F4}ms; bytes={allocated}; runtime={Environment.Version}");
        Assert.That(allocated, Is.Zero);
    }

    private static WaveStream Signal(bool hit, int seconds)
    {
        var values = new float[hit ? 88 : 44100 * 2 * seconds];
        if (hit) Array.Fill(values, .001f);
        return BufferedSampleTimingTests.Stream(values);
    }

    internal static void Report(string label, List<double> values)
    {
        values.Sort();
        TestContext.Out.WriteLine($"{label}: n={values.Count}; p95={values[(int)(values.Count * .95)]:F4}ms; p99={values[(int)(values.Count * .99)]:F4}ms; max={values[^1]:F4}ms");
    }
}
