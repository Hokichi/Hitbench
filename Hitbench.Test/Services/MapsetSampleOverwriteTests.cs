using Hitbench.Services.FileSystem;
namespace Hitbench.Test.Services;
public sealed class MapsetSampleOverwriteTests
{
    [TestCase(false)] [TestCase(true)]
    public async Task BlockedDestinationPreservesExistingData(bool directory)
    {
        var root = Directory.CreateTempSubdirectory("Hitbench-overwrite-blocked-");
        try
        {
            var source = Path.Combine(root.FullName, "drum-hitclap.wav");
            await File.WriteAllTextAsync(source, "new");
            var folder = root.CreateSubdirectory("Samples/Mapset");
            var target = Path.Combine(folder.FullName, "drum-hitclap.wav");
            if (directory) Directory.CreateDirectory(target); else await File.WriteAllTextAsync(target, "old");
            using (var locked = directory ? null : new FileStream(target, FileMode.Open, FileAccess.Read, FileShare.None))
            {
                var service = new SampleFileService(root.FullName);
                Assert.That(async () => await service.ImportAsync(Path.Combine(root.FullName, "project.json"), source, default, "Mapset", overwriteExisting: true), Throws.InstanceOf<IOException>().Or.InstanceOf<UnauthorizedAccessException>());
            }
            if (!directory) Assert.That(await File.ReadAllTextAsync(target), Is.EqualTo("old"));
            Assert.That(Directory.GetFileSystemEntries(folder.FullName), Has.Length.EqualTo(1));
        }
        finally { root.Delete(true); }
    }

    [Test]
    public async Task ImportingExistingFileIntoItsOwnFolderIsNoOp()
    {
        var root = Directory.CreateTempSubdirectory("Hitbench-overwrite-self-");
        try
        {
            var folder = root.CreateSubdirectory("Samples/Mapset");
            var source = Path.Combine(folder.FullName, "drum-hitclap.wav");
            await File.WriteAllTextAsync(source, "old");
            var result = await new SampleFileService(root.FullName).ImportAsync(Path.Combine(root.FullName, "project.json"), source, default, "Mapset", overwriteExisting: true);
            Assert.That(result, Is.EqualTo("Mapset/drum-hitclap.wav"));
            Assert.That(await File.ReadAllTextAsync(source), Is.EqualTo("old"));
        }
        finally { root.Delete(true); }
    }
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
