using Hitbench.Services;
using Hitbench.Resources.DataModels;
using Hitbench.Services.FileSystem;

namespace Hitbench.Test.Services;

public class OHsExportBudgetTests
{
    [Test]
    public async Task MixBudgetFailurePreservesPublishedExportAndReleasesDecodedSources()
    {
        using var folder = new TestProjectFolder("a.wav");
        var root = Path.GetDirectoryName(folder.ProjectPath)!;
        var map = Path.Combine(root, "Artist - Song (Mapper) [Original].osu");
        await File.WriteAllTextAsync(map, "[General]\nAudioFilename: song.wav\n[TimingPoints]\n0,500,4,1,1,100,1,0\n");
        var publisher = new OHsExportPublisher();
        var output = await publisher.PublishAsync(folder.ProjectPath, new Dictionary<string, string> { ["previous.osu"] = "difficulty" },
            (stage, token) => File.WriteAllTextAsync(Path.Combine(stage, "previous.osu"), "previous", token), default);
        var manifestPath = Path.Combine(root, ".osuhitsounder-export.json");
        var originalManifest = await File.ReadAllBytesAsync(manifestPath);
        var budget = new AudioMemoryBudget(80, 64);
        var project = new Hitbench.Core.Entities.ProjectData { Tracks = [new() { Volume = 50, Samples = [new() { FileName = "a.wav", Timestamp = 100 }] }] };
        var request = new OHsExportRequest(Guid.NewGuid(), folder.ProjectPath, map, "unused.wav",
            Hitbench.Services.ProjectFiles.ProjectDataSerializer.Serialize(project), new("Hitsounds", 1, ".wav", ".wav", false));
        var service = new OHsExportService(publisher, (_, _) => DecodedAudio.CopyFrom(new float[16]), budget);
        Assert.ThrowsAsync<AudioMemoryLimitException>(() => service.ExportAsync(request, default));
        Assert.That(await File.ReadAllTextAsync(Path.Combine(output, "previous.osu")), Is.EqualTo("previous"));
        Assert.That(await File.ReadAllBytesAsync(manifestPath), Is.EqualTo(originalManifest));
        Assert.That(budget.UsedBytes, Is.Zero);
        Assert.That(Directory.GetDirectories(root, ".Export-staging-*"), Is.Empty);
    }

    [Test]
    public void SourceFitsButMixDoesNotPreservesBorrowedSource()
    {
        var budget = new AudioMemoryBudget(64, 64);
        using var source = DecodedAudio.CopyFrom(new float[16], budget);
        var asset = OHsExportAsset.Create([("source.wav", 50)]);
        Assert.Throws<AudioMemoryLimitException>(() => OHsExportAudio.Render(asset, _ => source, default, budget));
        Assert.That(source.FrameCount, Is.EqualTo(8));
        Assert.That(budget.UsedBytes, Is.EqualTo(64));
    }

    [Test]
    public void MixAndCancellationReleaseOnlyTheirOwnedCharge()
    {
        var budget = new AudioMemoryBudget(128, 128);
        using var source = DecodedAudio.CopyFrom(Enumerable.Repeat(.5f, 16).ToArray(), budget);
        var asset = OHsExportAsset.Create([("source.wav", 50)]);
        using (var result = OHsExportAudio.Render(asset, _ => source, default, budget))
        {
            Assert.That(result.Samples[0], Is.EqualTo(.25f));
            Assert.That(budget.UsedBytes, Is.EqualTo(128));
        }
        using var cancel = new CancellationTokenSource(); cancel.Cancel();
        Assert.Catch<OperationCanceledException>(() => OHsExportAudio.Render(asset, _ => source, cancel.Token, budget));
        Assert.That(budget.UsedBytes, Is.EqualTo(64));
    }
}
