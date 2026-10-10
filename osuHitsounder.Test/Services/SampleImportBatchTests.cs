using osuHitsounder.Services.FileSystem;
using osuHitsounder.Test.Presentation;

namespace osuHitsounder.Test.Services;

public sealed class SampleImportBatchTests
{
    [Test] public async Task ImportBatchDeduplicatesAndContinuesAfterFailure()
    {
        var files = new StubSampleFileService([]) { Import = (_, path, _) => path == "bad.wav"
            ? Task.FromException<string>(new IOException("Denied")) : Task.FromResult(path) };
        var result = await new SampleImportBatch(files).ImportAsync("project.json", ["hit.wav", "HIT.wav", "bad.wav", "kick.ogg"], default);
        Assert.That(result.ImportedFiles, Is.EqualTo(new[] { "hit.wav", "kick.ogg" }));
        Assert.That(result.Failures.Single(), Does.Contain("bad.wav").And.Contain("Denied"));
        Assert.That(result.WasCancelled, Is.False);
    }
    [Test] public async Task CancelledBatchRetainsCompletedCopies()
    {
        using var cancellation = new CancellationTokenSource();
        var files = new StubSampleFileService([]) { Import = (_, path, _) =>
        { cancellation.Cancel(); return Task.FromResult(path); } };
        var result = await new SampleImportBatch(files).ImportAsync("project.json", ["hit.wav", "kick.wav"], cancellation.Token);
        Assert.That(result.ImportedFiles, Is.EqualTo(new[] { "hit.wav" }));
        Assert.That(result.WasCancelled, Is.True);
    }
}
