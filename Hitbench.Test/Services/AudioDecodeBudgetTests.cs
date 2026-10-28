using Hitbench.Services;
using NAudio.Wave;

namespace Hitbench.Test.Services;

public class AudioDecodeBudgetTests
{
    [Test]
    public void OneFrameReadsDoNotMultiplyChunkContainersAndReservations()
    {
        var bytes = new byte[131072];
        var budget = new AudioMemoryBudget(1000000, 1000000);
        DecodedAudio Decode() => AudioDecoder.Decode("unused", default,
            _ => new ShortReadWaveStream(bytes, WaveFormat.CreateIeeeFloatWaveFormat(44100, 2), maximumFramesPerRead: 1), budget);
        using (var warmup = Decode()) { }
        var before = GC.GetAllocatedBytesForCurrentThread();
        using var result = Decode();
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.That(result.FrameCount, Is.EqualTo(bytes.Length / 8));
        Assert.That(allocated, Is.LessThan(bytes.Length * 3L));
    }

    [TestCase(false)] [TestCase(true)]
    public void DecoderDisposalFailureReleasesPcmAndPreservesLimitFailure(bool exceedsLimit)
    {
        var budget = new AudioMemoryBudget(262144, exceedsLimit ? 4 : 64);
        DecodedAudio Decode() => AudioDecoder.Decode("unused", default,
            _ => new ShortReadWaveStream(new byte[16], WaveFormat.CreateIeeeFloatWaveFormat(44100, 2),
                onDispose: () => throw new IOException("decoder disposal failed")), budget);
        if (exceedsLimit) Assert.Throws<AudioMemoryLimitException>(() => Decode());
        else Assert.Throws<IOException>(() => Decode());
        Assert.That(budget.UsedBytes, Is.Zero);
    }

    [TestCase(262144, 4096, 8192)]
    [TestCase(180000, 65536, 32768)]
    public void WrongLengthHintAndFinalCopyCannotBypassBudget(long total, long asset, int inputBytes)
    {
        var budget = new AudioMemoryBudget(total, asset);
        var stream = new RawSourceWaveStream(new MemoryStream(new byte[inputBytes]), WaveFormat.CreateIeeeFloatWaveFormat(44100, 2));
        Assert.Throws<AudioMemoryLimitException>(() => AudioDecoder.Decode("unused.wav", default, _ => stream, budget));
        Assert.That(budget.UsedBytes, Is.Zero);
        Assert.That(budget.PeakBytes, Is.LessThanOrEqualTo(total));
    }

    [Test]
    public void SuccessfulDecodeTransfersChargeAndCancellationReleasesAll()
    {
        var budget = new AudioMemoryBudget(262144, 65536);
        using (var pcm = AudioDecoder.Decode("unused", default,
            _ => new ShortReadWaveStream(new byte[8192], WaveFormat.CreateIeeeFloatWaveFormat(44100, 2)), budget))
            Assert.That(budget.UsedBytes, Is.EqualTo(8192));
        Assert.That(budget.UsedBytes, Is.Zero);
        using var cancel = new CancellationTokenSource();
        Assert.Catch<OperationCanceledException>(() => AudioDecoder.Decode("unused", cancel.Token,
            _ => new ShortReadWaveStream(new byte[8192], WaveFormat.CreateIeeeFloatWaveFormat(44100, 2), cancel.Cancel), budget));
        Assert.That(budget.UsedBytes, Is.Zero);
    }

    [Test]
    public void Mp3GrowthRejectionKeepsCachedPrefixAndReleasesOnDispose()
    {
        var budget = new AudioMemoryBudget(65536, 64);
        var path = Directory.EnumerateFiles(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Audio"), "*.mp3").First();
        using (var stream = new OHsMp3WaveStream(path, budget))
        {
            var first = new byte[8]; stream.ReadExactly(first);
            Assert.Throws<AudioMemoryLimitException>(() => stream.ReadExactly(new byte[80]));
            stream.Position = 0;
            var repeated = new byte[8]; stream.ReadExactly(repeated);
            Assert.That(repeated, Is.EqualTo(first));
        }
        Assert.That(budget.UsedBytes, Is.Zero);
    }
}
