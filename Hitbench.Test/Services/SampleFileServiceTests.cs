using Hitbench.Services.FileSystem;

namespace Hitbench.Test.Services;

public class SampleFileServiceTests
{
    [Test]
    public async Task DirectSampleScanExcludesSubfolders()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")));
        try
        {
            var samples = Directory.CreateDirectory(Path.Combine(root.FullName, "Samples"));
            var piano = Directory.CreateDirectory(Path.Combine(samples.FullName, "Piano"));
            await File.WriteAllBytesAsync(Path.Combine(samples.FullName, "kick.WAV"), [1]);
            await File.WriteAllBytesAsync(Path.Combine(samples.FullName, "ignore.txt"), [1]);
            await File.WriteAllBytesAsync(Path.Combine(piano.FullName, "C3.wav"), [1]);
            var path = Path.Combine(root.FullName, "project.json");
            var service = new SampleFileService();
            Assert.That(await service.ListAsync(path, default, includeSubfolders: false), Is.EqualTo(new[] { "kick.WAV" }));
            Assert.That(await service.ListAsync(path, default), Is.EqualTo(new[] { "kick.WAV", "Piano/C3.wav" }));
            Assert.CatchAsync<OperationCanceledException>(() => service.ListAsync(path, new CancellationToken(true), false));
        }
        finally { Directory.Delete(root.FullName, true); }
    }

    [Test]
    public async Task ImportsDuplicateNamesWithoutOverwritingAndListsSupportedFiles()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        try
        {
            var projectDirectory = Directory.CreateDirectory(Path.Combine(root, "Project"));
            var projectPath = Path.Combine(projectDirectory.FullName, "project.json");
            await File.WriteAllTextAsync(projectPath, "{}");
            var samples = Directory.CreateDirectory(Path.Combine(projectDirectory.FullName, "Samples"));
            await File.WriteAllBytesAsync(Path.Combine(samples.FullName, "kick.wav"), [1]);
            await File.WriteAllBytesAsync(Path.Combine(samples.FullName, "kick (2).wav"), [2]);
            var source = Path.Combine(root, "kick.wav");
            await File.WriteAllBytesAsync(source, [3]);
            var service = new SampleFileService();
            var name = await service.ImportAsync(projectPath, source, CancellationToken.None);
            Assert.That(name, Is.EqualTo("kick (3).wav"));
            Assert.That(await File.ReadAllBytesAsync(Path.Combine(samples.FullName, "kick.wav")), Is.EqualTo(new byte[] { 1 }));
            Assert.That(await File.ReadAllBytesAsync(Path.Combine(samples.FullName, name)), Is.EqualTo(new byte[] { 3 }));
            Assert.That(await service.ListAsync(projectPath, CancellationToken.None), Is.EqualTo(new[] { "kick (2).wav", "kick (3).wav", "kick.wav" }));
        }
        finally { Directory.Delete(root, true); }
    }

    [Test]
    public async Task RejectsUnsupportedExtensionWithoutCopying()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")));
        try
        {
            var project = Path.Combine(root.FullName, "project.json");
            await File.WriteAllTextAsync(project, "{}");
            var source = Path.Combine(root.FullName, "sample.flac");
            await File.WriteAllBytesAsync(source, [1]);
            Assert.ThrowsAsync<InvalidDataException>(() => new SampleFileService().ImportAsync(project, source, CancellationToken.None));
            Assert.That(Directory.Exists(Path.Combine(root.FullName, "Samples")), Is.False);
        }
        finally { Directory.Delete(root.FullName, true); }
    }

    [Test]
    public async Task CancelledImportLeavesSamplesFolderEmpty()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")));
        try
        {
            var project = Path.Combine(root.FullName, "project.json");
            await File.WriteAllTextAsync(project, "{}");
            var source = Path.Combine(root.FullName, "sample.wav");
            await File.WriteAllBytesAsync(source, [1]);
            Assert.CatchAsync<OperationCanceledException>(() =>
                new SampleFileService().ImportAsync(project, source, new CancellationToken(true)));
            Assert.That(Directory.GetFiles(Path.Combine(root.FullName, "Samples")), Is.Empty);
        }
        finally { Directory.Delete(root.FullName, true); }
    }
}
