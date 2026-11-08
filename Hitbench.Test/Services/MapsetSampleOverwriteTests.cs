using Hitbench.Services.FileSystem;
namespace Hitbench.Test.Services;
public sealed class MapsetSampleOverwriteTests
{
    [TestCase("success")] [TestCase("cancel")] [TestCase("failure")]
    public async Task StagedReplacementPreservesOldBytesUntilPublication(string mode)
    {
        var root = Directory.CreateTempSubdirectory("Hitbench-overwrite-");
        using var cancellation = new CancellationTokenSource();
        try
        {
            var source = Path.Combine(root.FullName, "drum-hitclap.wav");
            await File.WriteAllTextAsync(source, "new bytes");
            var folder = root.CreateSubdirectory("Samples/Mapset");
            var target = Path.Combine(folder.FullName, "DRUM-hitclap.wav");
            await File.WriteAllTextAsync(target, "old bytes");
            var service = new SampleFileService(root.FullName, async (input, output, token) =>
            {
                await input.CopyToAsync(output, token);
                if (mode == "cancel") { cancellation.Cancel(); token.ThrowIfCancellationRequested(); }
                if (mode == "failure") throw new IOException("copy failed");
            });
            var operation = service.ImportAsync(Path.Combine(root.FullName, "project.json"), source, cancellation.Token, "Mapset", overwriteExisting: true);
            if (mode == "success") Assert.That(await operation, Is.EqualTo("Mapset/DRUM-hitclap.wav"));
            else if (mode == "cancel") Assert.ThrowsAsync<OperationCanceledException>(async () => await operation);
            else Assert.ThrowsAsync<IOException>(async () => await operation);
            Assert.That(await File.ReadAllTextAsync(target), Is.EqualTo(mode == "success" ? "new bytes" : "old bytes"));
            Assert.That(Directory.GetFiles(folder.FullName), Has.Length.EqualTo(1));
        }
        finally { root.Delete(true); }
    }
}
