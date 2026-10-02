using System.Diagnostics;
using osuHitsounder.Services;
using NAudio.Wave;
using osuHitsounder.Test.Presentation;

namespace osuHitsounder.Test.Services;

public class MarkerPlaybackStressTests
{
    [TestCase(64), TestCase(128)]
    public async Task FullSongTriggersThroughProductionMixer(int tracks)
    {
        using var project = new TestProjectFolder("hit.wav");
        var events = MarkerStressFixture.Create(tracks);
        var player = new StubWavePlayer();
        using var audio = new WorkstationAudioService(path => Signal(path.EndsWith("hit.wav"), 302), () => player);
        audio.Open("song.wav");
        await audio.UpdateSamplesAsync(project.ProjectPath, events, default);
        audio.Play();
        var output = new float[1500];
        var provider = player.Provider!.ToSampleProvider();
        var traversal = new MarkerTraversal();
        traversal.Replace(events);
        var clock = new PlaybackMarkerClock([0, 100000, 200000], [60, 120, 240]);
        var actual = new List<long>();
        var batch = new List<long>();
        for (var ms = 0; ms <= 300000; ms = Math.Min(300000, ms + 17))
        {
            traversal.Advance(clock.GetPosition(TimeSpan.FromMilliseconds(ms), TimeSpan.FromMinutes(5)), batch);
            audio.TriggerSamples(batch);
            actual.AddRange(batch);
            Assert.That(provider.Read(output, 0, output.Length), Is.EqualTo(output.Length));
            Assert.That(output[0], Is.EqualTo(Math.Min(1, batch.Count * .001f)).Within(.0001), $"Activation at {ms}ms");
            player.Position += output.Length * sizeof(float);
            if (ms == 300000) break;
        }
        Assert.That(actual, Is.EqualTo(events.OrderBy(e => e.Timestamp).ThenBy(e => e.Id).Select(e => e.Id)));
    }

    [TestCase(64), TestCase(128), Explicit, NonParallelizable]
    public async Task MeasureMarkerPlayback(int tracks)
    {
        var pooledSteps = new List<double>();
        var pooledLateness = new List<double>();
        for (var run = 1; run <= 3; run++)
        {
            using var project = new TestProjectFolder("hit.wav");
            var events = MarkerStressFixture.Create(tracks);
            var eligibility = events.ToDictionary(e => e.Id, e => EligibleMilliseconds(e.Timestamp));
            var clock = new Stopwatch();
            var player = new StubWavePlayer { PositionReader = () => (long)(clock.Elapsed.TotalSeconds * 44100) * 8, Playing = clock.Start };
            using var service = new WorkstationAudioService(path => Signal(path.EndsWith("hit.wav"), 62),
                () => new AudioOutputDevice(() => player, ownThread: true));
            var lateness = new List<double>();
            var actual = new List<long>();
            var steps = new List<double>();
            var measured = new MeasuredMarkerAudioService(service, ids =>
            {
                var now = clock.Elapsed.TotalMilliseconds;
                foreach (var id in ids) { actual.Add(id); if (now >= 1000) lateness.Add(now - eligibility[id]); }
            });
            long stepStart = 0;
            await using var coordinator = new MarkerPlaybackCoordinator(measured, async (duration, token) =>
            {
                if (stepStart != 0 && clock.Elapsed.TotalSeconds >= 1) steps.Add(Stopwatch.GetElapsedTime(stepStart).TotalMilliseconds);
                await MarkerPlaybackCoordinator.DelayAsync(duration, token).ConfigureAwait(false);
                stepStart = Stopwatch.GetTimestamp();
            });
            service.Open("song.wav");
            await coordinator.ConfigureAsync(new PlaybackMarkerClock([0, 20000, 40000], [60, 120, 240]), TimeSpan.FromSeconds(62), events, default);
            await coordinator.UpdateSamplesAsync(project.ProjectPath, events, default);
            await coordinator.PlayAsync(default);
            var provider = player.Provider!.ToSampleProvider();
            var output = new float[882]; // 10ms blocks; render 10ms ahead of the independent consumed clock.
            long rendered = 0;
            var audibleBlocks = 0;
            var allocations = GC.GetTotalAllocatedBytes();
            while (clock.Elapsed.TotalSeconds < 60)
            {
                var target = (long)((clock.Elapsed.TotalSeconds + .01) * 44100);
                while (rendered < target)
                {
                    var read = provider.Read(output, 0, output.Length);
                    if (read == 0) break;
                    rendered += read / 2;
                    if (output.Any(value => value > 0)) audibleBlocks++;
                }
                await MarkerPlaybackCoordinator.DelayAsync(TimeSpan.FromMilliseconds(1), CancellationToken.None).ConfigureAwait(false);
            }
            await coordinator.PauseAsync(default);
            allocations = GC.GetTotalAllocatedBytes() - allocations;
            var last = coordinator.Latest.Playback.Position.TotalMilliseconds;
            var lastEligibility = actual.Max(id => eligibility[id]);
            var expected = events.Where(e => eligibility[e.Id] <= lastEligibility)
                .OrderBy(e => e.Timestamp).ThenBy(e => e.Id).Select(e => e.Id);
            Assert.That(actual, Is.EqualTo(expected));
            Assert.That(audibleBlocks, Is.GreaterThan(0), "Production mixer must activate explicitly triggered voices.");
            TestContext.Out.WriteLine($"tracks={tracks}; run={run}; triggers={actual.Count}; bytes={allocations}; position={last:F2}; modeled-buffer=10-20ms; runtime={Environment.Version}; cpu={Environment.ProcessorCount}; OS={Environment.OSVersion}");
            Report("worker", steps); Report("lateness", lateness);
            pooledSteps.AddRange(steps); pooledLateness.AddRange(lateness);
        }
        Report($"pooled-worker-{tracks}", pooledSteps); Report($"pooled-lateness-{tracks}", pooledLateness);
    }

    private static double EligibleMilliseconds(int timestamp)
    {
        var origin = timestamp < 20000 ? 0 : timestamp < 40000 ? 20000 : 40000;
        var bpm = timestamp < 20000 ? 60 : timestamp < 40000 ? 120 : 240;
        var interval = 60000d / bpm / 120;
        return origin + Math.Ceiling((timestamp - origin) / interval - 1e-10) * interval;
    }

    internal static void Report(string label, List<double> values)
    {
        values.Sort();
        TestContext.Out.WriteLine($"{label}: n={values.Count}; p95={values[(int)(values.Count * .95)]:F4}ms; p99={values[(int)(values.Count * .99)]:F4}ms; max={values[^1]:F4}ms");
    }

    private static WaveStream Signal(bool hit, int seconds)
    {
        var values = new float[hit ? 88 : 44100 * 2 * seconds];
        if (hit) Array.Fill(values, .001f);
        var bytes = new byte[values.Length * 4];
        Buffer.BlockCopy(values, 0, bytes, 0, bytes.Length);
        return new RawSourceWaveStream(new MemoryStream(bytes), WaveFormat.CreateIeeeFloatWaveFormat(44100, 2));
    }
    [TestCase(8), TestCase(64), TestCase(128), Explicit]
    public void MeasureMixerBaseline(int tracks)
    {
        var events = MarkerStressFixture.Create(tracks);
        var hit = DecodedAudio.CopyFrom(new float[88]);
        var song = DecodedAudio.CopyFrom(new float[44100 * 2 * 10]);
        var schedule = new PreparedPlaybackSchedule(events.Select(e => new PreparedSampleEvent(e.Id, hit, 1, e.Timestamp * 441L / 10)).ToArray());
        var mixer = new ScheduledSampleMixer(song, 0);
        mixer.Publish(schedule);
        var output = new float[128];
        for (var i = 0; i < 100; i++) mixer.Read(output, 0, output.Length);
        var elapsed = new double[1000];
        var allocated = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < elapsed.Length; i++)
        {
            var start = Stopwatch.GetTimestamp();
            mixer.TriggerSamples([events[i].Id]);
            mixer.Read(output, 0, output.Length);
            elapsed[i] = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        }
        allocated = GC.GetAllocatedBytesForCurrentThread() - allocated;
        Array.Sort(elapsed);
        TestContext.Out.WriteLine($"tracks={tracks}; placements={events.Length}; p95={elapsed[950]:F4}ms; p99={elapsed[990]:F4}ms; max={elapsed[^1]:F4}ms; bytes={allocated}; runtime={Environment.Version}");
    }
}
