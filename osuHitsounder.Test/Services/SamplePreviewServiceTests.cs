using NAudio.Wave;
using osuHitsounder.Services;

namespace osuHitsounder.Test.Services;

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
