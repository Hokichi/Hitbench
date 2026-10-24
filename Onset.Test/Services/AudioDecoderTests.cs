using NAudio.Wave;
using Onset.Services;

namespace Onset.Test.Services;

public class AudioDecoderTests
{
    [Test]
    public void ShortReadsAndWrongLengthHintStillDecodeAllMonoFrames()
    {
        var values = Enumerable.Repeat(.25f, 22050).ToArray();
        var bytes = new byte[values.Length * 4];
        Buffer.BlockCopy(values, 0, bytes, 0, bytes.Length);
        var stream = new ShortReadWaveStream(bytes, WaveFormat.CreateIeeeFloatWaveFormat(22050, 1));
        var audio = AudioDecoder.Decode("unused", default, _ => stream);
        Assert.That(audio.FrameCount, Is.EqualTo(44100));
        Assert.That(stream.WasDisposed, Is.True);
        Assert.That(audio.Samples[2000], Is.EqualTo(.25f).Within(.001));
    }

    [Test]
    public void CancelledDecodeDisposesItsSource()
    {
        using var cancellation = new CancellationTokenSource();
        var stream = new ShortReadWaveStream(new byte[8192], WaveFormat.CreateIeeeFloatWaveFormat(44100, 2), cancellation.Cancel);
        Assert.Throws<OperationCanceledException>(() => AudioDecoder.Decode("unused", cancellation.Token, _ => stream));
        Assert.That(stream.WasDisposed, Is.True);
    }

    [Test]
    public void DecodeAvoidsTemporaryBlockArraysAndPreservesPcm()
    {
        var samples = Enumerable.Repeat(.25f, 44100 * 2 * 10).ToArray();
        var bytes = new byte[samples.Length * sizeof(float)];
        Buffer.BlockCopy(samples, 0, bytes, 0, bytes.Length);
        DecodedAudio Decode() => AudioDecoder.Decode("unused.wav", default,
            _ => new RawSourceWaveStream(new MemoryStream(bytes, false), WaveFormat.CreateIeeeFloatWaveFormat(44100, 2)));
        _ = Decode();
        var before = GC.GetAllocatedBytesForCurrentThread();
        var audio = Decode();
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.That(audio.FrameCount, Is.EqualTo(441000));
        Assert.That(audio.Samples.SequenceEqual(samples), Is.True);
        Assert.That(allocated, Is.LessThan(bytes.Length * 3L), $"allocated {allocated} for {bytes.Length} PCM bytes");
    }

    [Test]
    public void CopyFromKeepsOwnershipAndStereoValidation()
    {
        var source = new[] { .25f, .5f };
        var audio = DecodedAudio.CopyFrom(source);
        source[0] = 1;
        Assert.That(audio.Samples[0], Is.EqualTo(.25f));
        Assert.Throws<ArgumentException>(() => DecodedAudio.CopyFrom(new float[1]));
    }
}
