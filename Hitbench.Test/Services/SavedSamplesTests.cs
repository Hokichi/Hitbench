using Hitbench.Services.FileSystem;

namespace Hitbench.Test.Services;

public sealed class SavedSamplesTests
{
    private string root = null!, app = null!, project = null!, sample = null!;
    [SetUp]
    public void SetUp()
    {
        root = Directory.CreateTempSubdirectory("HitbenchSaved-").FullName;
        app = Directory.CreateDirectory(Path.Combine(root, "app")).FullName;
        project = Path.Combine(root, "Project", "project.json");
        var samples = Directory.CreateDirectory(Path.Combine(root, "Project", "Samples"));
        sample = Path.Combine(samples.FullName, "kick.wav");
        File.WriteAllBytes(sample, [1, 4, 9, 16]);
    }
    [TearDown] public void TearDown() => Directory.Delete(root, true);

    [Test]
    public async Task SaveCopiesBytesWithoutChangingProjectSample()
    {
        var path = await new SampleFileService(app).SaveAsync(project, "kick.wav", default);
        Assert.That(path, Is.EqualTo(Path.Combine(app, "saved", "kick.wav")));
        Assert.That(await File.ReadAllBytesAsync(path), Is.EqualTo(new byte[] { 1, 4, 9, 16 }));
        Assert.That(await File.ReadAllBytesAsync(sample), Is.EqualTo(new byte[] { 1, 4, 9, 16 }));
        Assert.That(Directory.Exists(Path.Combine(root, "Project", "Saved Samples")), Is.False);
    }

    [Test]
    public async Task SaveAllocatesNamesAcrossProjectsAndConcurrentRequests()
    {
        var other = Path.Combine(root, "Other", "project.json");
        Directory.CreateDirectory(Path.Combine(root, "Other", "Samples"));
        File.WriteAllBytes(Path.Combine(root, "Other", "Samples", "kick.wav"), [2]);
        var saved = Directory.CreateDirectory(Path.Combine(app, "saved"));
        Directory.CreateDirectory(Path.Combine(saved.FullName, "KICK.wav"));
        var first = new SampleFileService(app); var second = new SampleFileService(app);
        var results = await Task.WhenAll(first.SaveAsync(project, "kick.wav", default),
            second.SaveAsync(other, "kick.wav", default), first.SaveAsync(project, "kick.wav", default));
        Assert.That(results.Select(Path.GetFileName), Is.EquivalentTo(new[] { "kick (2).wav", "kick (3).wav", "kick (4).wav" }));
        Assert.That(results.Select(File.ReadAllBytes).Count(bytes => bytes.SequenceEqual(new byte[] { 2 })), Is.EqualTo(1));
        Assert.That(Directory.Exists(Path.Combine(saved.FullName, "KICK.wav")), Is.True);
    }

    [TestCase("../kick.wav")][TestCase("C:/kick.wav")][TestCase("kick.flac")]
    public void SaveRejectsUnsafePaths(string name) =>
        Assert.ThrowsAsync<ArgumentException>(() => new SampleFileService(app).SaveAsync(project, name, default));

    [Test]
    public void SaveRejectsLinkedProjectAncestors()
    {
        var linked = Path.Combine(root, "Linked");
        Directory.CreateSymbolicLink(linked, Path.Combine(root, "Project"));
        try
        {
            Assert.ThrowsAsync<ArgumentException>(() => new SampleFileService(app).SaveAsync(Path.Combine(linked, "project.json"), "kick.wav", default));
            Assert.That(File.ReadAllBytes(sample), Is.EqualTo(new byte[] { 1, 4, 9, 16 }));
        }
        finally { Directory.Delete(linked); }
    }

    [TestCase(false)][TestCase(true)]
    public void InterruptedSavePublishesNothing(bool cancel)
    {
        using var cancellation = new CancellationTokenSource();
        var files = new SampleFileService(app, async (_, output, token) =>
        {
            await output.WriteAsync(new byte[] { 1 }, token);
            if (cancel) { cancellation.Cancel(); token.ThrowIfCancellationRequested(); }
            throw new IOException("Interrupted copy");
        });
        if (cancel) Assert.CatchAsync<OperationCanceledException>(() => files.SaveAsync(project, "kick.wav", cancellation.Token));
        else Assert.ThrowsAsync<IOException>(() => files.SaveAsync(project, "kick.wav", default));
        Assert.That(Directory.GetFiles(Path.Combine(app, "saved")), Is.Empty);
        Assert.That(File.ReadAllBytes(sample), Is.EqualTo(new byte[] { 1, 4, 9, 16 }));
    }

    [Test]
    public void MissingSourceAndPrecancelledSaveLeaveNoPublishedFile()
    {
        var files = new SampleFileService(app);
        Assert.ThrowsAsync<FileNotFoundException>(() => files.SaveAsync(project, "missing.wav", default));
        Assert.CatchAsync<OperationCanceledException>(() => files.SaveAsync(project, "kick.wav", new CancellationToken(true)));
        Assert.That(Directory.GetFiles(Path.Combine(app, "saved")), Is.Empty);
    }

    [Test]
    public async Task MigrationPreservesNestedAndNonAudioFilesAndConflicts()
    {
        var legacy = Directory.CreateDirectory(Path.Combine(Path.GetDirectoryName(project)!, "Saved Samples")).FullName;
        Directory.CreateDirectory(Path.Combine(legacy, "Kit"));
        Directory.CreateDirectory(Path.Combine(legacy, "Empty"));
        File.WriteAllText(Path.Combine(legacy, "Kit", "notes.txt"), "notes");
        File.WriteAllText(Path.Combine(legacy, "kick.wav"), "old saved");
        var saved = Directory.CreateDirectory(Path.Combine(app, "saved")).FullName;
        File.WriteAllText(Path.Combine(saved, "KIT"), "existing file");
        File.WriteAllText(Path.Combine(saved, "KICK.wav"), "existing audio");
        var files = new SampleFileService(app);
        Assert.That(await files.TryMigrateSavedSamplesAsync(project, default), Is.True);
        Assert.That(Directory.Exists(legacy), Is.False);
        Assert.That(File.ReadAllText(Path.Combine(saved, "KIT")), Is.EqualTo("existing file"));
        Assert.That(File.ReadAllText(Path.Combine(saved, "Kit (2)", "notes.txt")), Is.EqualTo("notes"));
        Assert.That(File.ReadAllText(Path.Combine(saved, "KICK.wav")), Is.EqualTo("existing audio"));
        Assert.That(File.ReadAllText(Path.Combine(saved, "kick (2).wav")), Is.EqualTo("old saved"));
        Assert.That(Directory.Exists(Path.Combine(saved, "Empty")), Is.True);
        Assert.That(await files.TryMigrateSavedSamplesAsync(project, default), Is.True);
    }

    [Test]
    public async Task MigrationRetainsLockedAndLinkedSourcesAndRetries()
    {
        var legacy = Directory.CreateDirectory(Path.Combine(Path.GetDirectoryName(project)!, "Saved Samples")).FullName;
        var locked = Path.Combine(legacy, "locked.wav"); File.WriteAllText(locked, "keep locked");
        var link = Path.Combine(legacy, "linked");
        var outside = Directory.CreateDirectory(Path.Combine(root, "outside")).FullName;
        File.WriteAllText(Path.Combine(outside, "sentinel.txt"), "outside");
        Directory.CreateSymbolicLink(link, outside);
        var files = new SampleFileService(app);
        try
        {
            using (File.Open(locked, FileMode.Open, FileAccess.Read, FileShare.None))
                Assert.That(await files.TryMigrateSavedSamplesAsync(project, default), Is.False);
            Assert.That(File.ReadAllText(locked), Is.EqualTo("keep locked"));
            Assert.That(File.ReadAllText(Path.Combine(outside, "sentinel.txt")), Is.EqualTo("outside"));
        }
        finally { Directory.Delete(link); }
        Assert.That(await files.TryMigrateSavedSamplesAsync(project, default), Is.True);
        Assert.That(Directory.Exists(legacy), Is.False);
        Assert.That(File.ReadAllText(Path.Combine(app, "saved", "locked.wav")), Is.EqualTo("keep locked"));
    }

    [Test]
    public async Task MigrationCancellationPreservesUncopiedSources()
    {
        var legacy = Directory.CreateDirectory(Path.Combine(Path.GetDirectoryName(project)!, "Saved Samples")).FullName;
        File.WriteAllBytes(Path.Combine(legacy, "first.wav"), [1, 2]);
        File.WriteAllBytes(Path.Combine(legacy, "second.wav"), [3, 4]);
        using var cancellation = new CancellationTokenSource();
        var files = new SampleFileService(app, async (_, output, token) =>
        {
            await output.WriteAsync(new byte[] { 0 }, token);
            cancellation.Cancel(); token.ThrowIfCancellationRequested();
        });
        Assert.CatchAsync<OperationCanceledException>(() => files.TryMigrateSavedSamplesAsync(project, cancellation.Token));
        Assert.That(Directory.GetFiles(legacy).Length, Is.EqualTo(2));
        Assert.That(Directory.GetFiles(Path.Combine(app, "saved")), Is.Empty);
        Assert.That(await new SampleFileService(app).TryMigrateSavedSamplesAsync(project, default), Is.True);
        Assert.That(File.ReadAllBytes(Path.Combine(app, "saved", "second.wav")), Is.EqualTo(new byte[] { 3, 4 }));
    }

    [Test]
    public async Task MigrationRejectsOverlappingSourceAndLibrary()
    {
        var nestedProject = Path.Combine(app, "saved", "Project", "project.json");
        var legacy = Directory.CreateDirectory(Path.Combine(Path.GetDirectoryName(nestedProject)!, "Saved Samples")).FullName;
        var sentinel = Path.Combine(legacy, "keep.wav"); File.WriteAllText(sentinel, "keep");
        Assert.That(await new SampleFileService(app).TryMigrateSavedSamplesAsync(nestedProject, default), Is.False);
        Assert.That(File.ReadAllText(sentinel), Is.EqualTo("keep"));
        Assert.That(File.Exists(Path.Combine(app, "saved", "keep.wav")), Is.False);
    }
}
