using Hitbench.Services.FileSystem;

namespace Hitbench.Test.Services;

public sealed class OHsExportOwnershipTests
{
    private const string Manifest = ".osuhitsounder-export.json";
    private static string State(TestProjectFolder folder) => Path.Combine(Path.GetDirectoryName(folder.ProjectPath)!, Manifest);
    private static Task<string> Publish(OHsExportPublisher publisher, TestProjectFolder folder, string content) =>
        publisher.PublishAsync(folder.ProjectPath, new Dictionary<string, string> { ["a.wav"] = "sample" },
            (stage, token) => File.WriteAllTextAsync(Path.Combine(stage, "a.wav"), content, token), default);

    [Test]
    public async Task FreshExportKeepsOwnershipBesideProjectAndExportsNoJson()
    {
        using var folder = new TestProjectFolder();
        var output = await Publish(new(), folder, "old");
        Assert.That(Directory.GetFiles(output, "*.json", SearchOption.AllDirectories), Is.Empty);
        Assert.That(File.Exists(State(folder)), Is.True);
        await Publish(new(), folder, "new");
        Assert.That(await File.ReadAllTextAsync(Path.Combine(output, "a.wav")), Is.EqualTo("new"));
    }

    [TestCase(false)] [TestCase(true)]
    public async Task LegacyOwnershipMigratesOnlyAfterSuccessfulPublication(bool fail)
    {
        using var folder = new TestProjectFolder();
        var output = await Publish(new(), folder, "old");
        var legacy = Path.Combine(output, Manifest);
        if (File.Exists(State(folder))) File.Move(State(folder), legacy);
        var old = await File.ReadAllBytesAsync(legacy);
        var publisher = new OHsExportPublisher((from, to) =>
        { if (fail && to == output && Path.GetFileName(from).StartsWith(".Export-staging-")) throw new IOException("publish failed"); Directory.Move(from, to); });
        if (fail)
        {
            Assert.ThrowsAsync<IOException>(() => Publish(publisher, folder, "new"));
            var retained = output;
            Assert.That(await File.ReadAllBytesAsync(Path.Combine(retained, Manifest)), Is.EqualTo(old));
            Assert.That(File.Exists(State(folder)), Is.False);
        }
        else
        {
            await Publish(publisher, folder, "new");
            Assert.That(File.Exists(legacy), Is.False);
            Assert.That(File.Exists(State(folder)), Is.True);
        }
    }

    [TestCase(false)] [TestCase(true)]
    public async Task SidecarFailureRestoresExportAndPreservesOwnershipRecovery(bool failRecovery)
    {
        using var folder = new TestProjectFolder();
        var output = await Publish(new(), folder, "old");
        var old = await File.ReadAllBytesAsync(State(folder));
        var publisher = new OHsExportPublisher(Directory.Move, (from, to) =>
        {
            if (to == State(folder) && (Path.GetFileName(from).StartsWith(".Export-staging-") || failRecovery))
                throw new IOException("state move failed");
            File.Move(from, to);
        });
        var error = Assert.ThrowsAsync<IOException>(() => Publish(publisher, folder, "new"))!;
        Assert.That(await File.ReadAllTextAsync(Path.Combine(output, "a.wav")), Is.EqualTo("old"));
        if (failRecovery)
        {
            var backup = Directory.GetFiles(Path.GetDirectoryName(folder.ProjectPath)!, ".Export-backup-*-state.json").Single();
            Assert.That(await File.ReadAllBytesAsync(backup), Is.EqualTo(old));
            Assert.That(error.Message, Does.Contain(backup));
        }
        else Assert.That(await File.ReadAllBytesAsync(State(folder)), Is.EqualTo(old));
    }

    [Test]
    public async Task ConcurrentOwnershipEditRejectsPublication()
    {
        using var folder = new TestProjectFolder();
        var output = await Publish(new(), folder, "old");
        Assert.ThrowsAsync<IOException>(() => new OHsExportPublisher().PublishAsync(folder.ProjectPath,
            new Dictionary<string, string> { ["a.wav"] = "sample" }, async (stage, token) =>
            { await File.WriteAllTextAsync(Path.Combine(stage, "a.wav"), "new", token); await File.AppendAllTextAsync(State(folder), "\n", token); }, default));
        Assert.That(await File.ReadAllTextAsync(Path.Combine(output, "a.wav")), Is.EqualTo("old"));
    }

    [Test]
    public void FirstPublicationStateFailureLeavesNoPartialExport()
    {
        using var folder = new TestProjectFolder();
        var publisher = new OHsExportPublisher(Directory.Move, (_, _) => throw new IOException("state move failed"));
        Assert.ThrowsAsync<IOException>(() => Publish(publisher, folder, "new"));
        var root = Path.GetDirectoryName(folder.ProjectPath)!;
        Assert.That(Directory.Exists(Path.Combine(root, "Export")), Is.False);
        Assert.That(File.Exists(State(folder)), Is.False);
        Assert.That(Directory.GetFileSystemEntries(root, ".Export-*"), Is.Empty);
    }

    [Test]
    public async Task ModifiedLegacyOwnedFileRefusesMigration()
    {
        using var folder = new TestProjectFolder();
        var output = await Publish(new(), folder, "old");
        var legacy = Path.Combine(output, Manifest);
        File.Move(State(folder), legacy);
        var old = await File.ReadAllBytesAsync(legacy);
        await File.WriteAllTextAsync(Path.Combine(output, "a.wav"), "edited");
        Assert.ThrowsAsync<IOException>(() => Publish(new(), folder, "new"));
        Assert.That(await File.ReadAllBytesAsync(legacy), Is.EqualTo(old));
        Assert.That(await File.ReadAllTextAsync(Path.Combine(output, "a.wav")), Is.EqualTo("edited"));
        Assert.That(File.Exists(State(folder)), Is.False);
    }

    [Test]
    public void LinkedOwnershipIsRejectedWithoutTouchingTarget()
    {
        using var folder = new TestProjectFolder(); using var outside = new TestProjectFolder();
        var target = Path.Combine(Path.GetDirectoryName(outside.ProjectPath)!, "keep.json");
        File.WriteAllText(target, "outside"); File.CreateSymbolicLink(State(folder), target);
        try
        {
            Assert.ThrowsAsync<IOException>(() => Publish(new(), folder, "new"));
            Assert.That(File.ReadAllText(target), Is.EqualTo("outside"));
        }
        finally { File.Delete(State(folder)); }
    }
}
