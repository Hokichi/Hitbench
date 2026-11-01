using Hitbench.Services;

namespace Hitbench.Test.Services;

public class ControlPointRampPlaybackTests
{
    [Test]
    public void RampReadsAreSeekChunkAndStereoInvariant()
    {
        var expected = Read(0, [176400]);
        var chunked = Read(0, [1, 3, 17, 512, 8191]);
        Assert.That(chunked, Is.EqualTo(expected).Within(1e-6));
        Assert.That(Read(66150, [3, 17]), Is.EqualTo(expected.Skip(132300)).Within(1e-6));
        Assert.That(expected[88200], Is.EqualTo(.3f).Within(1e-6));
        Assert.That(expected[132300], Is.EqualTo(.4f).Within(1e-6));
    }

    private static float[] Read(long start, int[] chunks)
    {
        using var song = DecodedAudio.CopyFrom(new float[176400]);
        using var sample = DecodedAudio.CopyFrom(Enumerable.Repeat(.5f, 176400).ToArray());
        using var mixer = new ScheduledSampleMixer(song, start);
        mixer.Publish(new PreparedPlaybackSchedule([new(1, sample, 1)]));
        mixer.SetControlPoints(new([new(0, 20, IsRamp: true), new(2000, 100)]));
        var output = new float[176400 - checked((int)start * 2)];
        for (int offset = 0, i = 0; offset < output.Length; i++)
            offset += mixer.Read(output, offset, Math.Min(chunks[i % chunks.Length], output.Length - offset));
        return output;
    }
}
