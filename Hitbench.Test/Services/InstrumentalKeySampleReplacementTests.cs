using Hitbench.Services.FileSystem;

namespace Hitbench.Test.Services;

public sealed class InstrumentalKeySampleReplacementTests
{
    private string root = null!, project = null!, folder = null!, source = null!, destination = null!;
    [SetUp] public void Setup()
    {
        root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"))).FullName;
        project = Path.Combine(root, "project.json");
        folder = Directory.CreateDirectory(Path.Combine(root, "Samples", "Piano")).FullName;
        source = Path.Combine(root, "hit.wav"); destination = Path.Combine(folder, "C4.wav");
        File.WriteAllBytes(source, [2, 3]); File.WriteAllBytes(destination, [1]);
    }
    [TearDown] public void Cleanup() => Directory.Delete(root, true);

    [TestCase(".wav")][TestCase(".OGG")][TestCase(".mp3")]
    public async Task CopiesToExactKeyAndPreservesSourceAndOtherExtensions(string extension)
    {
        source = Path.ChangeExtension(source, extension); File.WriteAllBytes(source, [2, 3]);
        var result = await new SampleFileService().ImportKeyAsync(project, source, "Piano", "C#4", default);
        Assert.That(result, Is.EqualTo("Piano/C#4" + extension));
        Assert.That(File.ReadAllBytes(Path.Combine(folder, "C#4" + extension)), Is.EqualTo(new byte[] { 2, 3 }));
        Assert.That(File.ReadAllBytes(source), Is.EqualTo(new byte[] { 2, 3 }));
        Assert.That(File.ReadAllBytes(destination), Is.EqualTo(new byte[] { 1 }));
        Assert.That(Directory.GetFiles(folder, "*.tmp"), Is.Empty);
    }
    [Test] public async Task OverwritesCaseInsensitiveDestinationWithoutSuffix()
    {
        var otherExtension = Path.Combine(folder, "C4.ogg"); File.WriteAllBytes(otherExtension, [4]);
        File.Move(destination, Path.Combine(folder, "c4.WAV"));
        Assert.That(await new SampleFileService().ImportKeyAsync(project, source, "Piano", "C4", default), Is.EqualTo("Piano/C4.wav"));
        Assert.That(File.ReadAllBytes(destination), Is.EqualTo(new byte[] { 2, 3 }));
        Assert.That(File.ReadAllBytes(otherExtension), Is.EqualTo(new byte[] { 4 }));
        Assert.That(Directory.GetFiles(folder), Has.Length.EqualTo(2));
    }
    [Test] public async Task SourceInsideTrackFolderIsCopiedAndDestinationItselfIsNotRewritten()
    {
        source = Path.Combine(folder, "hit.wav"); File.WriteAllBytes(source, [2, 3]);
        var service = new SampleFileService();
        await service.ImportKeyAsync(project, source, "Piano", "C4", default);
        var timestamp = File.GetLastWriteTimeUtc(destination);
        await service.ImportKeyAsync(project, destination, "Piano", "C4", default);
        Assert.That(File.GetLastWriteTimeUtc(destination), Is.EqualTo(timestamp));
        Assert.That(File.ReadAllBytes(source), Is.EqualTo(new byte[] { 2, 3 }));
    }
    [TestCase(false)][TestCase(true)] public void InterruptedCopyRetainsOldDestination(bool cancel)
    {
        using var cancellation = new CancellationTokenSource();
        var copied = false;
        var service = new SampleFileService(root, async (input, output, token) =>
        {
            copied = true;
            await output.WriteAsync(new byte[] { 9 }, token);
            if (cancel) { cancellation.Cancel(); token.ThrowIfCancellationRequested(); }
            throw new IOException("copy failed");
        });
        if (cancel) Assert.CatchAsync<OperationCanceledException>(() => service.ImportKeyAsync(project, source, "Piano", "C4", cancellation.Token));
        else Assert.ThrowsAsync<IOException>(() => service.ImportKeyAsync(project, source, "Piano", "C4", cancellation.Token));
        Assert.That(copied, Is.True);
        Assert.That(File.ReadAllBytes(destination), Is.EqualTo(new byte[] { 1 }));
        Assert.That(Directory.GetFiles(folder, "*.tmp"), Is.Empty);
    }
    [Test] public void FailedFinalizationRestoresOldBytes()
    {
        var service = new SampleFileService(root, (input, output, token) => input.CopyToAsync(output, token), (_, _) => throw new IOException("rename failed"));
        Assert.ThrowsAsync<IOException>(() => service.ImportKeyAsync(project, source, "Piano", "C4", default));
        Assert.That(File.ReadAllBytes(destination), Is.EqualTo(new byte[] { 1 }));
        Assert.That(Directory.GetFiles(folder, "*.tmp"), Is.Empty);
    }
    [Test] public void FailedRestorationKeepsRecoverableBackup()
    {
        var service = new SampleFileService(root, (input, output, token) => input.CopyToAsync(output, token), (_, target) =>
        { Directory.CreateDirectory(target); throw new IOException("rename failed"); });
        var error = Assert.ThrowsAsync<IOException>(() => service.ImportKeyAsync(project, source, "Piano", "C4", default));
        var backup = Directory.GetFiles(folder, "*.tmp").Single();
        Assert.That(error!.Message, Does.Contain(backup));
        Assert.That(File.ReadAllBytes(backup), Is.EqualTo(new byte[] { 1 }));
    }
    [Test] public async Task CancellationAfterCommitPointStillCompletes()
    {
        using var cancel = new CancellationTokenSource();
        var service = new SampleFileService(root, (input, output, token) => input.CopyToAsync(output, token), (staged, target) =>
        { cancel.Cancel(); File.Move(staged, target); });
        Assert.That(await service.ImportKeyAsync(project, source, "Piano", "C4", cancel.Token), Is.EqualTo("Piano/C4.wav"));
        Assert.That(File.ReadAllBytes(destination), Is.EqualTo(new byte[] { 2, 3 }));
    }
    [TestCase("../Piano", "C4")][TestCase("Piano", "C0")]
    public void InvalidDestinationCannotMutateFiles(string name, string key)
    {
        Assert.CatchAsync<ArgumentException>(() => new SampleFileService().ImportKeyAsync(project, source, name, key, default));
        Assert.That(File.ReadAllBytes(destination), Is.EqualTo(new byte[] { 1 }));
    }
    [Test] public void MissingUnsupportedAndCancelledSourcesRetainDestination()
    {
        var service = new SampleFileService();
        Assert.ThrowsAsync<FileNotFoundException>(() => service.ImportKeyAsync(project, source + ".wav", "Piano", "C4", default));
        Assert.ThrowsAsync<InvalidDataException>(() => service.ImportKeyAsync(project, source + ".flac", "Piano", "C4", default));
        Assert.CatchAsync<OperationCanceledException>(() => service.ImportKeyAsync(project, source, "Piano", "C4", new(true)));
        Assert.That(File.ReadAllBytes(destination), Is.EqualTo(new byte[] { 1 }));
    }
    [TestCase(true)][TestCase(false)] public void LinkedSourceOrDestinationIsRejected(bool linkSource)
    {
        var link = linkSource ? Path.Combine(root, "link.wav") : Path.Combine(folder, "D4.wav");
        File.CreateSymbolicLink(link, source);
        Assert.CatchAsync<ArgumentException>(() => new SampleFileService().ImportKeyAsync(project, linkSource ? link : source, "Piano", linkSource ? "C4" : "D4", default));
        Assert.That(File.ReadAllBytes(source), Is.EqualTo(new byte[] { 2, 3 }));
    }
    [Test] public void DestinationDirectoryIsNotDeleted()
    {
        File.Delete(destination); Directory.CreateDirectory(destination);
        Assert.ThrowsAsync<IOException>(() => new SampleFileService().ImportKeyAsync(project, source, "Piano", "C4", default));
        Assert.That(Directory.Exists(destination), Is.True);
    }
}
