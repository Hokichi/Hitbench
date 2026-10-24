using Onset.Services;

namespace Onset.Test.Services;

public class ScheduledSampleMixerTests
{
    [Test]
    public void IndexedRangesMatchExhaustiveOverlapAndRetainEarlyLongTail()
    {
        var random = new Random(4242);
        var events = Enumerable.Range(1, 1000).Select(i => Event(i, i * 3,
            new float[random.Next(1, 90) * 2])).Prepend(Event(1001, 0, new float[8000])).ToArray();
        var schedule = new PreparedPlaybackSchedule(events);
        for (var start = 0; start < 5000; start += 19)
        {
            var range = schedule.GetOverlappingRange(start, start + 23);
            var candidates = schedule.Events[range.Start..range.End].ToArray().Where(e => e.EndFrame > start);
            Assert.That(candidates.Select(e => e.Id), Is.EquivalentTo(events.Where(e => e.StartFrame < start + 23 && e.EndFrame > start).Select(e => e.Id)));
        }
        Assert.That(schedule.GetOverlappingRange(40, 40), Is.EqualTo((0, 0)));
    }

    [Test]
    public void LateReadSkipsExpiredPrefix()
    {
        var hit = DecodedAudio.CopyFrom(new float[2]);
        var schedule = new PreparedPlaybackSchedule(Enumerable.Range(1, 128000).Select(i => new PreparedSampleEvent(i, hit, 1, i)).ToArray());
        var range = schedule.GetOverlappingRange(128001, 128100);
        Assert.That(range.End - range.Start, Is.Zero);
    }
    [Test]
    public void PublicationStartsSampleAtItsTimestamp()
    {
        var mixer = new ScheduledSampleMixer(DecodedAudio.CopyFrom(new float[8]), 0);
        mixer.Publish(new PreparedPlaybackSchedule([Event(1, 0, [.5f, .5f])]));
        var output = new float[8];
        mixer.Read(output, 0, output.Length);
        Assert.That(output, Is.EqualTo(new[] { .5f, .5f, 0f, 0f, 0f, 0f, 0f, 0f }));
    }

    [Test]
    public void PastPublicationDoesNotReplayExhaustedAttack()
    {
        var mixer = new ScheduledSampleMixer(DecodedAudio.CopyFrom(new float[20]), 0);
        mixer.Read(new float[6], 0, 6);
        mixer.Publish(new PreparedPlaybackSchedule([Event(1, 0, [.5f, .6f, .3f, .4f])]));
        var output = new float[6];
        mixer.Read(output, 0, output.Length);
        Assert.That(output, Is.All.Zero);
    }

    [Test]
    public void NewSampleBetweenChannelsStartsAtNextCompleteFrame()
    {
        var mixer = new ScheduledSampleMixer(DecodedAudio.CopyFrom(new float[8]), 0);
        mixer.Read(new float[1], 0, 1);
        mixer.Publish(new PreparedPlaybackSchedule([Event(1, 1, [.5f, .6f])]));
        var output = new float[3];
        mixer.Read(output, 0, 3);
        Assert.That(output, Is.EqualTo(new[] { 0f, .5f, .6f }));
    }

    [Test]
    public void ResumeAtConsumedFrameRetainsRenderedButUnheardTail()
    {
        var song = DecodedAudio.CopyFrom(new float[20]);
        var schedule = new PreparedPlaybackSchedule([Event(1, 0, [.1f,.1f,.2f,.2f,.3f,.3f,.4f,.4f])]);
        var mixer = new ScheduledSampleMixer(song, 0);
        mixer.Publish(schedule);
        mixer.Read(new float[16], 0, 16);
        var resumed = new ScheduledSampleMixer(song, 2);
        resumed.Publish(schedule);
        var output = new float[6];
        resumed.Read(output, 0, 6);
        Assert.That(output, Is.EqualTo(new[] { .3f,.3f,.4f,.4f,0f,0f }));
    }

    [Test]
    public async Task ConcurrentPublicationNeverMixesVersionsWithinRead()
    {
        var mixer = new ScheduledSampleMixer(DecodedAudio.CopyFrom(new float[200000]),0);
        var first = new PreparedPlaybackSchedule([Event(1,0,Enumerable.Repeat(.2f,200000).ToArray())]);
        var second = new PreparedPlaybackSchedule([Event(1,0,Enumerable.Repeat(.4f,200000).ToArray())]);
        mixer.Publish( first);
        using var started = new ManualResetEventSlim();
        var publisher = Task.Run(()=>
        {
            started.Set();
            for(var i=0;i<10000;i++) mixer.Publish(i%2==0?first:second);
        });
        started.Wait();
        var buffer = new float[1024];
        for(var i=0;i<100;i++)
        {
            Assert.That(mixer.Read(buffer,0,buffer.Length),Is.EqualTo(buffer.Length));
            Assert.That(buffer[0],Is.EqualTo(.2f).Or.EqualTo(.4f));
            Assert.That(buffer,Is.All.EqualTo(buffer[0]));
        }
        await publisher;
    }

    [TestCase(1)]
    [TestCase(37)]
    [TestCase(64)]
    [TestCase(512)]
    [TestCase(1024)]
    public void ImpulseStartsAtExactFrameWithoutExternalTrigger(int chunk)
    {
        var mixer = new ScheduledSampleMixer(DecodedAudio.CopyFrom(new float[4096]), 0);
        mixer.Publish( new PreparedPlaybackSchedule([Event(1, 1000, [.5f, .5f])]));
        var output = new float[4096];
        for (var i = 0; i < output.Length;)
            i += mixer.Read(output, i, Math.Min(chunk * 2, output.Length - i));
        var expected = new float[4096];
        expected[2000] = expected[2001] = .5f;
        Assert.That(output, Is.EqualTo(expected));
        Assert.That(mixer.EndOfStream, Is.True);
        Assert.That(mixer.Read(output, 0, 2), Is.Zero);
    }

    [Test]
    public void PartialFrameKeepsOriginalRightChannel()
    {
        var mixer = new ScheduledSampleMixer(DecodedAudio.CopyFrom(new float[4]), 0);
        mixer.Publish( new PreparedPlaybackSchedule([Event(1, 0, [.2f, .3f, .4f, .5f])]));
        var output = new float[4];
        mixer.Read(output, 0, 1);
        Assert.That(mixer.RenderedFrames, Is.Zero);
        mixer.Publish(new PreparedPlaybackSchedule([Event(2, 0, [.8f, .8f, .7f, .6f])]));
        mixer.Read(output, 1, 3);
        Assert.That(output, Is.EqualTo(new[] { .2f, .3f, .7f, .6f }));
        Assert.That(mixer.RenderedFrames, Is.EqualTo(2));
    }

    [Test]
    public void SeekAndNegativeTailsRespectSourceOffsetAndSongEnd()
    {
        var mixer = new ScheduledSampleMixer(DecodedAudio.CopyFrom(new float[6]), 1);
        mixer.Publish( new PreparedPlaybackSchedule([Event(1, -1, [.1f,.1f,.2f,.2f,.3f,.3f,.4f,.4f,.5f,.5f])]));
        var output = Enumerable.Repeat(-9f, 8).ToArray();
        Assert.That(mixer.Read(output, 2, 6), Is.EqualTo(4));
        Assert.That(output, Is.EqualTo(new[] {-9f,-9f,.3f,.3f,.4f,.4f,-9f,-9f}));
    }

    [Test]
    public void OverlappingSamplesHaveNoVoiceLimitAndGainPrecedesClipping()
    {
        var mixer = new ScheduledSampleMixer(DecodedAudio.CopyFrom([float.NaN, float.PositiveInfinity]), 0);
        mixer.Publish( new PreparedPlaybackSchedule(Enumerable.Range(1, 64).Select(id => Event(id, 0, [.4f,.4f], .5f)).ToArray()));
        mixer.SetVolumes(1, .5f, .05f);
        var output = new float[2];
        mixer.Read(output, 0, 2);
        Assert.That(output, Is.EqualTo(new[] { .32f,.32f }).Within(.00001));
    }

    [Test]
    public void ScheduleAndAudioOwnTheirInputAndRejectInvalidData()
    {
        float[] data = [.5f,.5f];
        var audio = DecodedAudio.CopyFrom(data);
        data[0] = 9;
        var events = new[] { new PreparedSampleEvent(1, audio, 1) };
        var schedule = new PreparedPlaybackSchedule(events);
        events[0] = Event(2, 0, [0,0]);
        var mixer = new ScheduledSampleMixer(DecodedAudio.CopyFrom(new float[2]), 0);
        mixer.Publish( schedule);
        var output = new float[2];
        mixer.Read(output,0,2);
        Assert.That(output, Is.EqualTo(new[] {.5f,.5f}));
        Assert.Throws<ArgumentException>(() => new PreparedPlaybackSchedule([Event(1,0,[0,0]),Event(1,1,[0,0])]));
        Assert.Throws<ArgumentException>(() => DecodedAudio.CopyFrom([1]));
        Assert.Throws<ArgumentException>(() => new PreparedPlaybackSchedule([Event(1,0,[0,0],float.NaN)]));
        Assert.Throws<OverflowException>(() => new PreparedPlaybackSchedule([Event(1,long.MaxValue,[0,0])]));
    }

    [TestCase(false), TestCase(true)]
    public void NormalReadAllocatesNothingAndChecksBounds(bool activeControlPoints)
    {
        var mixer = new ScheduledSampleMixer(DecodedAudio.CopyFrom(new float[400_100]),0);
        mixer.Publish( new PreparedPlaybackSchedule([Event(1,0,new float[400_100])]));
        if (activeControlPoints) mixer.SetControlPoints(new([new(0, 50), new(1000, 25), new(2000, 100)]));
        var output = new float[16];
        long allocated = -1;
        Exception? measurementFailure = null;
        // Isolate allocation measurement from the test runner's reused-thread state.
        var worker = new Thread(() =>
        {
            try
            {
                // Keep background-GC counter effects outside the strict zero-byte measurement.
                if (!GC.TryStartNoGCRegion(16 * 1024 * 1024))
                    throw new InvalidOperationException("Cannot isolate the allocation measurement from background GC.");
                try
                {
                    _ = MeasureReads(mixer, output);
                    allocated = MeasureReads(mixer, output);
                }
                finally { GC.EndNoGCRegion(); }
            }
            catch (Exception error) { measurementFailure = error; }
        });
        worker.Start();
        worker.Join();
        if (measurementFailure is not null)
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(measurementFailure).Throw();
        Assert.That(allocated, Is.Zero);
        Assert.That(mixer.Faulted, Is.False);
        Assert.That(mixer.EndOfStream, Is.False);
        Assert.That(mixer.Read(output,0,0), Is.Zero);
        Assert.Throws<ArgumentOutOfRangeException>(()=>mixer.Read(output,1,16));
    }

    private static PreparedSampleEvent Event(long id,long start,float[] values,float gain=1) =>
        new(id,DecodedAudio.CopyFrom(values),gain,start);

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static long MeasureReads(ScheduledSampleMixer mixer, float[] output)
    {
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 10_000; i++) mixer.Read(output, 0, output.Length);
        return GC.GetAllocatedBytesForCurrentThread() - before;
    }
}
