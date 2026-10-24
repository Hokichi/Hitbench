using NAudio.Wave;
using Hitbench.Services;

namespace Hitbench.Test.Services;

public sealed class SamplePreviewServiceTests
{
    private string path = "";
    [SetUp]
    public void CreateAudio()
    {
        path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".wav");
        using var writer = new WaveFileWriter(path, WaveFormat.CreateIeeeFloatWaveFormat(44_100, 1));
        writer.WriteSamples(Enumerable.Repeat(1f, 4410).ToArray(), 0, 4410);
    }
    [TearDown] public void DeleteAudio() => File.Delete(path);

    [Test]
    public async Task PlayAtOffsetReportsPositionAndDuration()
    {
        var output = new StubAudioOutput();
        await using var preview = new SamplePreviewService(AudioDecoder.Open, [new("Test", () => output)]);
        await preview.PlayAsync(path, 100, default, TimeSpan.FromSeconds(.05));
        var state = await preview.GetSnapshotAsync(default);
        Assert.That(state.Position.TotalSeconds, Is.EqualTo(.05).Within(1d / 44100));
        Assert.That(state.Duration.TotalSeconds, Is.EqualTo(.1).Within(1d / 44100));
        output.Source!.Read(new float[1000], 0, 1000);
        output.PlayedFrames = 441;
        Assert.That((await preview.GetSnapshotAsync(default)).Position.TotalSeconds, Is.EqualTo(.06).Within(1d / 44100));
    }

    [Test]
    public async Task NonSeekableDecoderFailsCleanlyAtOffset()
    {
        await using var preview = new SamplePreviewService(_ => new NonSeekingWaveStream(),
            [new("Test", () => new StubAudioOutput())]);
        await preview.PlayAsync(path, 100, default);
        Assert.CatchAsync<InvalidOperationException>(() => preview.SeekAsync(TimeSpan.FromSeconds(.03), default));
        Assert.That((await preview.GetSnapshotAsync(default)).IsPlaying, Is.False);
    }

    [TestCase(-1, 0)]
    [TestCase(.06, .06)]
    [TestCase(1, .1)]
    public async Task SeekReplacesBufferedOutputAndClampsPosition(double target, double expected)
    {
        var outputs = new List<StubAudioOutput>();
        await using var preview = new SamplePreviewService(AudioDecoder.Open,
            [new("Test", () => { var output = new StubAudioOutput(); outputs.Add(output); return output; })]);
        await preview.PlayAsync(path, 25, default);
        await preview.SeekAsync(TimeSpan.FromSeconds(target), default);
        var state = await preview.GetSnapshotAsync(default);
        Assert.That(outputs[0].IsDisposed, Is.True);
        Assert.That(state.Position.TotalSeconds, Is.EqualTo(expected).Within(1d / 44100));
        if (expected < .1)
        {
            var data = new float[1];
            Assert.That(outputs[^1].Source!.Read(data, 0, 1), Is.EqualTo(1));
            Assert.That(data[0], Is.EqualTo(.25f));
        }
    }

    [Test]
    public async Task NaturalCompletionRetainsFinalPosition()
    {
        var output = new StubAudioOutput();
        await using var preview = new SamplePreviewService(AudioDecoder.Open, [new("Test", () => output)]);
        await preview.PlayAsync(path, 100, default);
        output.Complete();
        var state = await preview.GetSnapshotAsync(default);
        Assert.That(state.Position, Is.EqualTo(state.Duration));
        Assert.That(state.Duration, Is.EqualTo(TimeSpan.FromSeconds(.1)));
        Assert.That(state.IsPlaying, Is.False);
    }

    [Test]
    public async Task StopResetsPositionAndReleasesReader()
    {
        await using var preview = new SamplePreviewService(AudioDecoder.Open, [new("Test", () => new StubAudioOutput())]);
        await preview.PlayAsync(path, 100, default, TimeSpan.FromSeconds(.04));
        await preview.StopAsync(default);
        var state = await preview.GetSnapshotAsync(default);
        Assert.That(state.Position, Is.EqualTo(TimeSpan.Zero));
        Assert.That(state.Duration, Is.EqualTo(TimeSpan.Zero));
        using var exclusive = File.Open(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
    }

    [Test]
    public async Task CancelledOrFailedSeekReleasesResources()
    {
        var output = new StubAudioOutput();
        var fail = false;
        await using var preview = new SamplePreviewService(AudioDecoder.Open,
            [new("Test", () => fail ? throw new IOException("Disconnected") : output)]);
        await preview.PlayAsync(path, 100, default);
        Assert.CatchAsync<OperationCanceledException>(() => preview.SeekAsync(TimeSpan.Zero, new CancellationToken(true)));
        fail = true;
        Assert.CatchAsync<InvalidOperationException>(() => preview.SeekAsync(TimeSpan.FromSeconds(.03), default));
        Assert.That(output.IsDisposed, Is.True);
        using var exclusive = File.Open(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
    }

    [Test]
    public async Task PlayUsesApplicationVolumeAndStopReleasesReader()
    {
        var output = new StubAudioOutput();
        await using var preview = new SamplePreviewService(AudioDecoder.Open, [new("Test", () => output)]);
        await preview.PlayAsync(path, 25, CancellationToken.None);
        var data = new float[4];
        output.Source!.Read(data, 0, data.Length);
        Assert.That(data[0], Is.EqualTo(0.25f).Within(0.0001));
        await preview.StopAsync(CancellationToken.None);
        Assert.That((await preview.GetSnapshotAsync(CancellationToken.None)).IsPlaying, Is.False);
        using var exclusive = File.Open(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
    }

    [Test]
    public async Task ReplayingStartsAtBeginningAndReplacesPreviousOutput()
    {
        var outputs = new List<StubAudioOutput>();
        await using var preview = new SamplePreviewService(AudioDecoder.Open, [new("Test", () => { var output = new StubAudioOutput(); outputs.Add(output); return output; })]);
        await preview.PlayAsync(path, 100, CancellationToken.None);
        outputs[0].Source!.Read(new float[400], 0, 400);
        await preview.PlayAsync(path, 100, CancellationToken.None);
        Assert.That(outputs[0].IsDisposed, Is.True);
        var data = new float[1];
        Assert.That(outputs[1].Source!.Read(data, 0, 1), Is.EqualTo(1));
        Assert.That(data[0], Is.EqualTo(1));
    }

    [Test]
    public async Task NaturalCompletionReturnsStoppedSnapshot()
    {
        var output = new StubAudioOutput();
        await using var preview = new SamplePreviewService(AudioDecoder.Open, [new("Test", () => output)]);
        await preview.PlayAsync(path, 100, CancellationToken.None);
        output.Complete();
        Assert.That((await preview.GetSnapshotAsync(CancellationToken.None)).IsPlaying, Is.False);
        Assert.That(output.IsDisposed, Is.True);
        using var exclusive = File.Open(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
    }

    [Test]
    public async Task InitializationFailureFallsBackAndReportsWarning()
    {
        var preferred = new StubAudioOutput { FailInitialization = true };
        var fallback = new StubAudioOutput();
        await using var preview = new SamplePreviewService(AudioDecoder.Open, [new("Preferred", () => preferred), new("Fallback", () => fallback)]);
        await preview.PlayAsync(path, 100, CancellationToken.None);
        var snapshot = await preview.GetSnapshotAsync(CancellationToken.None);
        Assert.That(snapshot.IsPlaying, Is.True);
        Assert.That(snapshot.Warning, Does.Contain("Fallback"));
        Assert.That(preferred.IsDisposed, Is.True);
    }

    [Test]
    public async Task OutputFailureAndCancellationReleaseResources()
    {
        var output = new StubAudioOutput();
        await using var preview = new SamplePreviewService(AudioDecoder.Open, [new("Test", () => output)]);
        await preview.PlayAsync(path, 100, CancellationToken.None);
        output.Complete(new IOException("Disconnected"));
        Assert.That((await preview.GetSnapshotAsync(CancellationToken.None)).Error, Does.Contain("Disconnected"));
        Assert.CatchAsync<OperationCanceledException>(() => preview.PlayAsync(path, 100, new CancellationToken(true)));
        using var exclusive = File.Open(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
    }

    [Test]
    public async Task RetiredOutputCannotStopNewPreview()
    {
        var old = new StubAudioOutput();
        var current = new StubAudioOutput();
        var calls = 0;
        await using var preview = new SamplePreviewService(AudioDecoder.Open, [new("Test", () => calls++ == 0 ? old : current)]);
        await preview.PlayAsync(path, 100, CancellationToken.None);
        await preview.PlayAsync(path, 100, CancellationToken.None);
        old.RaiseRetiredStopped();
        Assert.That((await preview.GetSnapshotAsync(CancellationToken.None)).IsPlaying, Is.True);
    }
}
