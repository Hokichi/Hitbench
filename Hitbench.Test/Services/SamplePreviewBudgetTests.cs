using Hitbench.Services;
using NAudio.Wave;

namespace Hitbench.Test.Services;

public class SamplePreviewBudgetTests
{
    [Test]
    public async Task StopAndDisposeFailuresStillRetireReaderAndPreserveOriginalError()
    {
        var reader = new ShortReadWaveStream(new byte[8192], WaveFormat.CreateIeeeFloatWaveFormat(44100, 2));
        var output = new StubAudioOutput { FailStop = true, FailDispose = true };
        await using var preview = new SamplePreviewService(_ => reader, [new("Test", () => output)]);
        await preview.PlayAsync("sample.wav", 100, default);
        output.Complete(new IOException("original failure"));
        var result = await preview.GetSnapshotAsync(default);
        Assert.That(result.Error, Does.Contain("original failure"));
        Assert.That(reader.WasDisposed, Is.True);
        Assert.That(output.IsDisposed, Is.True);
        await preview.StopAsync(default);
    }

    [Test]
    public async Task NativePreviewCannotBorrowBeyondOtherLiveAudioOwners()
    {
        var budget = new AudioMemoryBudget(65536, 65536);
        using var occupied = budget.Reserve(65536);
        var path = Directory.EnumerateFiles(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Audio"), "*.mp3").First();
        await using var preview = new SamplePreviewService(file => AudioDecoder.Open(file, budget), [new("Test", () => new StubAudioOutput())]);
        Assert.ThrowsAsync<InvalidOperationException>(() => preview.PlayAsync(path, 100, default));
        Assert.That(budget.UsedBytes, Is.EqualTo(65536));
        await preview.StopAsync(default);
    }
}
