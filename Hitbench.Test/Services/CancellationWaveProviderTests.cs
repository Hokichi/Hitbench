using NAudio.Wave;
using Hitbench.Services;
namespace Hitbench.Test.Services;

public class CancellationWaveProviderTests
{
    [Test]
    public void ReadsUntilCancelledAndKeepsOriginalFormat()
    {
        using var cancellation = new CancellationTokenSource();
        var source = new SilenceProvider(new WaveFormat(44100, 16, 2));
        var provider = new CancellationWaveProvider(source, cancellation.Token);
        Assert.That(provider.WaveFormat, Is.SameAs(source.WaveFormat));
        Assert.That(provider.Read(new byte[16], 0, 16), Is.EqualTo(16));
        cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() => provider.Read(new byte[16], 0, 16));
    }
    [Test]
    public void Mp3EncoderFailureIsObserved()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".mp3");
        Assert.ThrowsAsync<NotSupportedException>(() => OHsExportAudio.WriteFileAsync(path,
            DecodedAudio.CopyFrom(new float[] { .1f, .1f }), default, (_, _) => throw new NotSupportedException("Encoder unavailable")));
        Assert.That(File.Exists(path), Is.False);
    }
}
