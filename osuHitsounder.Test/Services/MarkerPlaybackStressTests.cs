using System.Diagnostics;
using osuHitsounder.Services;

namespace osuHitsounder.Test.Services;

public class MarkerPlaybackStressTests
{
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
