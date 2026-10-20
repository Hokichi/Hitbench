using osuHitsounder.Services.FileSystem;
namespace osuHitsounder.Test.Services;

public class OHsExportPublisherTests
{
    private static Task<string> Publish(OHsExportPublisher publisher, string project, string name, string content) => publisher.PublishAsync(project,
        new Dictionary<string, string> { [name] = "sample" }, (stage, token) => File.WriteAllTextAsync(Path.Combine(stage, name), content, token), default);

    [Test]
    public async Task ReexportRemovesOwnedStaleFilesAndPreservesUnrelatedContent()
    {
        using var folder = new TestProjectFolder(); var publisher = new OHsExportPublisher();
        var output = await Publish(publisher, folder.ProjectPath, "old.wav", "old");
        Directory.CreateDirectory(Path.Combine(output, "notes", "empty"));
        await File.WriteAllTextAsync(Path.Combine(output, "notes", "keep.txt"), "mine");
        await Publish(publisher, folder.ProjectPath, "new.ogg", "new");
        Assert.That(File.Exists(Path.Combine(output, "old.wav")), Is.False);
        Assert.That(await File.ReadAllTextAsync(Path.Combine(output, "new.ogg")), Is.EqualTo("new"));
        Assert.That(await File.ReadAllTextAsync(Path.Combine(output, "notes", "keep.txt")), Is.EqualTo("mine"));
        Assert.That(Directory.Exists(Path.Combine(output, "notes", "empty")), Is.True);
    }
    [TestCase(false)] [TestCase(true)]
    public async Task CollisionsPreserveExistingFiles(bool previouslyOwned)
    {
        using var folder = new TestProjectFolder(); var publisher = new OHsExportPublisher();
        var output = Path.Combine(Path.GetDirectoryName(folder.ProjectPath)!, "Export");
        if (previouslyOwned) await Publish(publisher, folder.ProjectPath, "a.wav", "old");
        else Directory.CreateDirectory(output);
        await File.WriteAllTextAsync(Path.Combine(output, "a.wav"), "edited");
        Assert.ThrowsAsync<IOException>(() => Publish(publisher, folder.ProjectPath, "a.wav", "new"));
        Assert.That(await File.ReadAllTextAsync(Path.Combine(output, "a.wav")), Is.EqualTo("edited"));
    }
    [TestCase(false)] [TestCase(true)]
    public async Task FailedSecondRenameRestoresOrPreservesCompleteBackup(bool failRecovery)
    {
        using var folder = new TestProjectFolder();
        var output = await Publish(new(), folder.ProjectPath, "a.wav", "old");
        var moves = 0;
        var publisher = new OHsExportPublisher((from, to) =>
        {
            moves++;
            if (moves == 2 || failRecovery && moves == 3) throw new IOException("Injected rename failure");
            Directory.Move(from, to);
        });
        var error = Assert.ThrowsAsync<IOException>(() => Publish(publisher, folder.ProjectPath, "a.wav", "new"))!;
        if (!failRecovery) Assert.That(await File.ReadAllTextAsync(Path.Combine(output, "a.wav")), Is.EqualTo("old"));
        else
        {
            var backup = Directory.GetDirectories(Path.GetDirectoryName(folder.ProjectPath)!, ".Export-backup-*").Single();
            Assert.That(await File.ReadAllTextAsync(Path.Combine(backup, "a.wav")), Is.EqualTo("old"));
            Assert.That(error.Message, Does.Contain(backup));
        }
    }
    [Test]
    public async Task CancellationAndConcurrentEditCannotPublishOverOldExport()
    {
        using var folder = new TestProjectFolder(); var publisher = new OHsExportPublisher();
        var output = await Publish(publisher, folder.ProjectPath, "a.wav", "old");
        Assert.ThrowsAsync<IOException>(() => publisher.PublishAsync(folder.ProjectPath, new Dictionary<string, string> { ["a.wav"] = "sample" }, async (stage, token) =>
        { await File.WriteAllTextAsync(Path.Combine(stage, "a.wav"), "new", token); await File.WriteAllTextAsync(Path.Combine(output, "user.txt"), "concurrent", token); }, default));
        Assert.That(await File.ReadAllTextAsync(Path.Combine(output, "a.wav")), Is.EqualTo("old"));
        using var cancellation = new CancellationTokenSource();
        Assert.ThrowsAsync<OperationCanceledException>(() => publisher.PublishAsync(folder.ProjectPath, new Dictionary<string, string> { ["a.wav"] = "sample" }, (stage, token) =>
        { cancellation.Cancel(); return Task.CompletedTask; }, cancellation.Token));
        Assert.That(await File.ReadAllTextAsync(Path.Combine(output, "user.txt")), Is.EqualTo("concurrent"));
    }
    [TestCase("../outside.wav")] [TestCase("C:\\outside.wav")]
    public void UnsafeInventoryFailsBeforeBuilding(string name)
    {
        using var folder = new TestProjectFolder();
        Assert.ThrowsAsync<ArgumentException>(() => new OHsExportPublisher().PublishAsync(folder.ProjectPath, new Dictionary<string, string> { [name] = "sample" }, (_, _) => throw new AssertionException("Unsafe builder ran"), default));
    }
}
