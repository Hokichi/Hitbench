using Onset.Services.FileSystem;
namespace Onset.Test.Services;

public class OHsExportPublisherTests
{
    [Test]
    public async Task CleanupDoesNotReadBackupContentsAfterPublication()
    {
        using var project = new TestProjectFolder();
        var root = Path.GetDirectoryName(project.ProjectPath)!;
        var files = new Dictionary<string, string> { ["owned.wav"] = "sample" };
        Task Build(string stage, CancellationToken token) => File.WriteAllTextAsync(Path.Combine(stage, "owned.wav"), "data", token);
        var output = await new Onset.Services.FileSystem.OHsExportPublisher().PublishAsync(project.ProjectPath, files, Build, default);
        FileStream? hold = null;
        try
        {
            var publisher = new Onset.Services.FileSystem.OHsExportPublisher((from, to) =>
            {
                Directory.Move(from, to);
                if (Path.GetFileName(to).StartsWith(".Export-backup-", StringComparison.Ordinal))
                    hold = new FileStream(Path.Combine(to, "owned.wav"), FileMode.Open, FileAccess.Read, FileShare.Delete);
            });
            await publisher.PublishAsync(project.ProjectPath, files, Build, default);
            Assert.That(Directory.GetDirectories(root, ".Export-backup-*"), Is.Empty);
            Assert.That(File.Exists(Path.Combine(output, "owned.wav")), Is.True);
        }
        finally { hold?.Dispose(); }
    }

    [Test]
    public async Task FullLengthValidDifficultyStemCanBePublished()
    {
        using var folder = new TestProjectFolder();
        var name = new string('a', 251) + ".osu";
        var output = await Publish(new(), folder.ProjectPath, name, "difficulty");
        Assert.That(await File.ReadAllTextAsync(Path.Combine(output, name)), Is.EqualTo("difficulty"));
    }
    [TestCase(false)] [TestCase(true)]
    public void LinkedOutputOrUnrelatedFileIsRejected(bool directory)
    {
        using var folder = new TestProjectFolder(); using var outside = new TestProjectFolder();
        var output = Path.Combine(Path.GetDirectoryName(folder.ProjectPath)!, "Export");
        var target = Path.GetDirectoryName(outside.ProjectPath)!;
        File.WriteAllText(Path.Combine(target, "keep.txt"), "outside");
        var link = directory ? output : Path.Combine(output, "link.txt");
        if (directory) Directory.CreateSymbolicLink(link, target);
        else { Directory.CreateDirectory(output); File.CreateSymbolicLink(link, Path.Combine(target, "keep.txt")); }
        try
        {
            Assert.ThrowsAsync<IOException>(() => Publish(new(), folder.ProjectPath, "a.wav", "new"));
            Assert.That(File.ReadAllText(Path.Combine(target, "keep.txt")), Is.EqualTo("outside"));
        }
        finally { if (directory) Directory.Delete(link); else File.Delete(link); }
    }
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
    public async Task BackupCleanupFailureStillReturnsSuccessfullyPublishedDirectory()
    {
        using var folder = new TestProjectFolder();
        var output = await Publish(new(), folder.ProjectPath, "a.wav", "old");
        string? backup = null; FileStream? locked = null;
        var publisher = new OHsExportPublisher((from, to) =>
        {
            Directory.Move(from, to);
            if (Path.GetFileName(to).StartsWith(".Export-backup-")) backup = to;
            else if (to == output && backup is not null) locked = File.Open(Path.Combine(backup, "a.wav"), FileMode.Open, FileAccess.Read, FileShare.None);
        });
        try
        {
            Assert.That(await Publish(publisher, folder.ProjectPath, "a.wav", "new"), Is.EqualTo(output));
            Assert.That(await File.ReadAllTextAsync(Path.Combine(output, "a.wav")), Is.EqualTo("new"));
            Assert.That(Directory.Exists(backup), Is.True);
        }
        finally { locked?.Dispose(); }
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

    [Test]
    public async Task DuplicateCircleFailurePreservesPreviousPublication()
    {
        using var folder = new TestProjectFolder();
        var publisher = new OHsExportPublisher();
        var output = await Publish(publisher, folder.ProjectPath, "old.osu", "previous");
        var manifest = await File.ReadAllBytesAsync(Path.Combine(output, ".osuhitsounder-export.json"));
        var map = Onset.Resources.DataModels.OHsExportBeatmap.Parse("[TimingPoints]\n0,500,4,1,1,100,1,0\n");
        var input = new Onset.Resources.DataModels.OHsExportInput(new("Hitsounds", 1, ".wav", ".wav", false), map, [new(100, "a.wav", 100, "Soft", Onset.Core.Enums.SampleType.Clap, 0)], map.TimingPoints);
        var plan = Onset.Services.OHsExportPlanner.Plan(input, default);
        var invalid = new Onset.Resources.DataModels.OHsExportPlan([plan.Circles[0], plan.Circles[0]], plan.TimingPoints, plan.Banks, plan.AdditionalPointCount);
        Assert.ThrowsAsync<InvalidDataException>(() => publisher.PublishAsync(folder.ProjectPath,
            new Dictionary<string, string> { ["new.osu"] = "difficulty" },
            (stage, token) => File.WriteAllTextAsync(Path.Combine(stage, "new.osu"), Onset.Services.OHsExportBeatmapWriter.Write(input, invalid), token), default));
        Assert.That(await File.ReadAllTextAsync(Path.Combine(output, "old.osu")), Is.EqualTo("previous"));
        Assert.That(await File.ReadAllBytesAsync(Path.Combine(output, ".osuhitsounder-export.json")), Is.EqualTo(manifest));
        Assert.That(Directory.GetDirectories(Path.GetDirectoryName(folder.ProjectPath)!, ".Export-staging-*"), Is.Empty);
    }
}
