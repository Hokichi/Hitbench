using NAudio.Wave;
using osuHitsounder.Services;

namespace osuHitsounder.Test.Services;

public class ScheduledSampleGainTests
{
    private static readonly WaveFormat Format = WaveFormat.CreateIeeeFloatWaveFormat(1_000, 2);

    [Test]
    public void EventAtBufferBoundaryStartsInFollowingBufferOnce()
    {
        var mixer = CreateMixer(new float[48]);
        PublishActivated(mixer, new PreparedPlaybackSchedule([Event(1, 10, [0.4f, 0.4f])]));

        var first = new float[20];
        var second = new float[4];

        Assert.That(mixer.Read(first, 0, first.Length), Is.EqualTo(20));
        Assert.That(first, Is.All.EqualTo(0f));
        Assert.That(mixer.Read(second, 0, second.Length), Is.EqualTo(4));
        Assert.That(second, Is.EqualTo(new[] { 0.4f, 0.4f, 0f, 0f }));
    }

    [Test]
    public void SongAndSampleHaveIndependentGain()
    {
        var mixer = CreateMixer([0.2f, 0.2f]);
        mixer.SetVolumes(.5f,1,1);
        PublishActivated(mixer, new PreparedPlaybackSchedule([Event(1, 0, [0.4f, 0.4f], 0.25f)]));
        var output = new float[2];

        mixer.Read(output, 0, output.Length);

        Assert.That(output, Is.EqualTo(new[] { 0.2f, 0.2f }).Within(0.0001));
    }

    [Test]
    public void MutedSongDoesNotMuteSamples()
    {
        var mixer = CreateMixer([0.2f, 0.2f]);
        mixer.SetVolumes(0,1,1);
        PublishActivated(mixer, new PreparedPlaybackSchedule([Event(1, 0, [0.4f, 0.4f], 0.25f)]));
        var output = new float[2];

        mixer.Read(output, 0, output.Length);

        Assert.That(output, Is.EqualTo(new[] { 0.1f, 0.1f }).Within(0.0001));
    }

    [Test]
    public void MasterVolumesMultiplyTheirOwnBusesBeforeApplicationGain()
    {
        var mixer = CreateMixer([0.2f, 0.2f]);
        mixer.SetVolumes(.5f,.5f,.5f);
        PublishActivated(mixer, new PreparedPlaybackSchedule([Event(1, 0, [0.4f, 0.4f], 0.5f)]));
        var output = new float[2];

        mixer.Read(output, 0, output.Length);

        Assert.That(output, Is.EqualTo(new[] { 0.1f, 0.1f }).Within(0.0001));
    }

    [Test]
    public void ApplicationGainIsAppliedBeforeFinalClipping()
    {
        var mixer = CreateMixer([0f, 0f]);
        mixer.SetVolumes(1,1,.5f);
        PublishActivated(mixer, new PreparedPlaybackSchedule([
            Event(1, 0, [0.8f, 0.8f]),
            Event(2, 0, [0.8f, 0.8f])
        ]));
        var output = new float[2];

        mixer.Read(output, 0, output.Length);

        Assert.That(output, Is.EqualTo(new[] { 0.8f, 0.8f }).Within(0.0001));
    }

    [Test]
    public void ChangingSamplesVolumeUpdatesAnActiveVoiceWithoutLosingItsPosition()
    {
        var mixer = CreateMixer(new float[6]);
        PublishActivated(mixer, new PreparedPlaybackSchedule([Event(1, 0, Enumerable.Repeat(0.4f, 6).ToArray())]));
        var output = new float[6];

        mixer.Read(output, 0, 2);
        mixer.SetVolumes(1,0,1);
        mixer.Read(output, 2, 2);
        mixer.SetVolumes(1,.5f,1);
        mixer.Read(output, 4, 2);

        Assert.That(output, Is.EqualTo(new[] { 0.4f, 0.4f, 0f, 0f, 0.2f, 0.2f }).Within(0.0001));
    }

    [Test]
    public void MasterVolumesClampAndRejectNonFiniteValues()
    {
        var mixer = CreateMixer([1f, 1f]);

        mixer.SetVolumes(1,4,float.NaN);
        var output = new float[2];
        mixer.Read(output,0,2);
        Assert.That(output, Is.All.Zero);
    }

    [Test]
    public void SimultaneousOverlappingEventsClampFiniteOutput()
    {
        var mixer = CreateMixer([0.8f, float.NaN, 0, 0]);
        PublishActivated(mixer, new PreparedPlaybackSchedule([
            Event(1, 0, [0.8f, 0.8f, 0.8f, 0.8f]),
            Event(2, 0, [0.8f, 0.8f, float.PositiveInfinity, -4f])
        ]));
        var output = new float[4];

        mixer.Read(output, 0, output.Length);

        Assert.That(output, Is.EqualTo(new[] { 1f, 1f, 0.8f, -1f }));
    }

    [Test]
    public void ReadHonorsDestinationOffsetAndCutsTailsAtSongEnd()
    {
        var mixer = CreateMixer(new float[4]);
        PublishActivated(mixer, new PreparedPlaybackSchedule([Event(1, 1, [0.5f, 0.5f, 0.5f, 0.5f])]));
        var output = Enumerable.Repeat(-9f, 8).ToArray();

        var read = mixer.Read(output, 2, 2);
        read += mixer.Read(output, 4, 4);

        Assert.Multiple(() =>
        {
            Assert.That(read, Is.EqualTo(4));
            Assert.That(output[..2], Is.All.EqualTo(-9f));
            Assert.That(output[2..6], Is.EqualTo(new[] { 0f, 0f, 0.5f, 0.5f }));
            Assert.That(output[6..], Is.All.EqualTo(-9f));
        });
    }

    [Test]
    public void ReplacingSamplesUpdatesActiveGainRemovesVoicesWithoutStartingNewOnes()
    {
        var mixer = CreateMixer(new float[12]);
        PublishActivated(mixer, new PreparedPlaybackSchedule([Event(1, 0, Enumerable.Repeat(0.5f, 12).ToArray())]));
        var first = new float[2];
        mixer.Read(first, 0, first.Length);

        mixer.Publish(new PreparedPlaybackSchedule([
            Event(1, 0, Enumerable.Repeat(0.5f, 12).ToArray(), 0.2f),
            Event(2, 0, [1f, 1f])
        ]));
        var second = new float[2];
        mixer.Read(second, 0, second.Length);
        mixer.Publish(new PreparedPlaybackSchedule([]));
        var third = new float[2];
        mixer.Read(third, 0, third.Length);

        Assert.Multiple(() =>
        {
            Assert.That(first, Is.EqualTo(new[] { 0.5f, 0.5f }));
            Assert.That(second, Is.EqualTo(new[] { 0.1f, 0.1f }).Within(0.0001));
            Assert.That(third, Is.All.EqualTo(0f));
        });
    }

    [Test]
    public void ChunkSizeDoesNotChangeMixedSamples()
    {
        var oneRead = CreateMixer(new float[12]);
        var chunked = CreateMixer(new float[12]);
        var schedule = new[] { Event(1, 1, [0.1f, 0.2f, 0.3f, 0.4f]), Event(2, 2, [0.5f, 0.6f]) };
        PublishActivated(oneRead, new PreparedPlaybackSchedule(schedule));
        PublishActivated(chunked, new PreparedPlaybackSchedule(schedule));
        var expected = new float[12];
        var actual = new float[12];

        oneRead.Read(expected, 0, 12);
        chunked.Read(actual, 0, 4);
        chunked.Read(actual, 4, 2);
        chunked.Read(actual, 6, 6);

        Assert.That(actual, Is.EqualTo(expected).Within(0.0001));
    }

    private static ScheduledSampleMixer CreateMixer(float[] song, long startFrame = 0) =>
        new(DecodedAudio.CopyFrom(song), startFrame);

    private static PreparedSampleEvent Event(long id, long frame, float[] samples, float gain = 1) =>
        new(id, DecodedAudio.CopyFrom(samples), gain, frame);
    private static void PublishActivated(ScheduledSampleMixer mixer, PreparedPlaybackSchedule schedule)
    {
        mixer.Publish(schedule);
        mixer.RestoreVoices(schedule.Events.ToArray().Select(item => new SampleVoiceActivation(item.Id, item.StartFrame)).ToArray());
    }

}
