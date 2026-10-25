using Hitbench.Services.FileSystem;
using Hitbench.Services.ProjectFiles;

namespace Hitbench.Test.Services;

public sealed class ProjectCreationPublicationTests
{
    private string root = null!;
    private string appRoot = null!;
    private DirectoryInfo song = null!;
    private string? link;
    private string Target => Path.Combine(appRoot, "projects", "Retry");

    [SetUp]
    public void SetUp()
    {
        root = Directory.CreateTempSubdirectory("Hitbench-publication-").FullName;
        appRoot = Path.Combine(root, "app");
        song = Directory.CreateDirectory(Path.Combine(root, "song"));
        File.WriteAllText(Path.Combine(song.FullName, "map.osu"), "[TimingPoints]\n0,500,4,1,0,100,1,0\n");
    }

    [TearDown]
    public void TearDown()
    {
        if (link is not null && Directory.Exists(link)) Directory.Delete(link);
        Directory.Delete(root, true);
    }

    [Test]
    public async Task UnpublishedProjectIsAbsentFromNamesAndHomeInventory()
    {
        var writing = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var service = new ProjectFileService(appRoot, async (path, json, token) =>
        {
            await File.WriteAllTextAsync(path, json, token);
            writing.SetResult();
            await release.Task;
        }, Directory.Move);
        var operation = service.CreateAsync(song, "Retry", default);
        try
        {
            await writing.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.That(await service.GetExistingNamesAsync(default), Is.Empty);
            Assert.That((await new HomeFolderService(appRoot).LoadProjectsAsync(default)).Count, Is.Zero);
        }
        finally { release.TrySetResult(); await operation; }
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task WriteFailureOrCancellationAllowsSameNameRetry(bool cancel)
    {
        using var cancellation = new CancellationTokenSource();
        var service = new ProjectFileService(appRoot, async (path, json, token) =>
        {
            await File.WriteAllTextAsync(path, json, token);
            if (cancel) { cancellation.Cancel(); token.ThrowIfCancellationRequested(); }
            throw new IOException("write failed");
        }, Directory.Move);
        if (cancel) Assert.CatchAsync<OperationCanceledException>(() => service.CreateAsync(song, "Retry", cancellation.Token));
        else Assert.ThrowsAsync<IOException>(() => service.CreateAsync(song, "Retry", default));
        Assert.That(Directory.Exists(Target), Is.False);
        Assert.That(await service.GetExistingNamesAsync(default), Is.Empty);
        var path = await new ProjectFileService(appRoot).CreateAsync(song, "Retry", default);
        Assert.That(File.Exists(path), Is.True);
        Assert.That(Directory.EnumerateDirectories(appRoot, ".project-staging-*"), Is.Empty);
    }

    [Test]
    public async Task PublicationFailureRemovesOwnedStaging()
    {
        var attempted = false;
        var service = new ProjectFileService(appRoot, File.WriteAllTextAsync, (_, _) =>
        {
            attempted = true;
            throw new IOException("publication failed");
        });
        Assert.ThrowsAsync<IOException>(() => service.CreateAsync(song, "Retry", default));
        Assert.That(attempted, Is.True);
        Assert.That(Directory.Exists(Target), Is.False);
        Assert.That(Directory.EnumerateDirectories(appRoot, ".project-staging-*"), Is.Empty);
        Assert.That(File.Exists(await new ProjectFileService(appRoot).CreateAsync(song, "Retry", default)), Is.True);
    }

    [Test]
    public async Task ConcurrentSameNamePublicationPreservesWinner()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var count = 0;
        var service = new ProjectFileService(appRoot, async (path, json, token) =>
        {
            await File.WriteAllTextAsync(path, json, token);
            if (Interlocked.Increment(ref count) == 2) entered.SetResult();
            await release.Task;
        }, Directory.Move);
        var first = service.CreateAsync(song, "Retry", default);
        var second = service.CreateAsync(song, "Retry", default);
        try { await entered.Task.WaitAsync(TimeSpan.FromSeconds(5)); }
        finally { release.TrySetResult(); }
        var results = await Task.WhenAll(OutcomeAsync(first), OutcomeAsync(second));
        Assert.That(results.Count(error => error is null), Is.EqualTo(1));
        Assert.That(results.Count(error => error is IOException), Is.EqualTo(1));
        Assert.That(ProjectDataSerializer.Deserialize(await File.ReadAllTextAsync(Path.Combine(Target, "project.json"))).BPMs,
            Is.EqualTo(new[] { 120 }));
        Assert.That(Directory.EnumerateDirectories(appRoot, ".project-staging-*"), Is.Empty);
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task ExistingFileOrEmptyDirectoryIsPreserved(bool directory)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Target)!);
        if (directory) Directory.CreateDirectory(Target);
        else await File.WriteAllTextAsync(Target, "keep");
        Assert.ThrowsAsync<IOException>(() => new ProjectFileService(appRoot).CreateAsync(song, "Retry", default));
        if (directory) Assert.That(Directory.GetFileSystemEntries(Target), Is.Empty);
        else Assert.That(await File.ReadAllTextAsync(Target), Is.EqualTo("keep"));
    }

    [Test]
    public async Task CancellationAfterCommitReturnsCommittedPath()
    {
        using var cancellation = new CancellationTokenSource();
        var service = new ProjectFileService(appRoot, File.WriteAllTextAsync, (stage, destination) =>
        {
            Directory.Move(stage, destination);
            cancellation.Cancel();
        });
        var path = await service.CreateAsync(song, "Retry", cancellation.Token);
        Assert.That(cancellation.IsCancellationRequested, Is.True);
        Assert.That(File.Exists(path), Is.True);
        Assert.That(Directory.Exists(Path.Combine(Target, "Saved Samples")), Is.False);
    }

    [Test]
    public void LinkedStagingCleanupPreservesOutsideSentinel()
    {
        var outside = Directory.CreateDirectory(Path.Combine(root, "outside")).FullName;
        var sentinel = Path.Combine(outside, "keep.txt");
        File.WriteAllText(sentinel, "keep");
        var service = new ProjectFileService(appRoot, (path, _, _) =>
        {
            link = Path.Combine(Path.GetDirectoryName(path)!, "linked");
            Directory.CreateSymbolicLink(link, outside);
            throw new IOException("write failed");
        }, Directory.Move);
        var error = Assert.Throws<AggregateException>(() => service.CreateAsync(song, "Retry", default).GetAwaiter().GetResult());
        Assert.That(error!.Message, Does.Contain(".project-staging-"));
        Assert.That(File.ReadAllText(sentinel), Is.EqualTo("keep"));
        Assert.That(Directory.Exists(Target), Is.False);
    }

    private static async Task<Exception?> OutcomeAsync(Task<string> operation)
    {
        try { await operation; return null; }
        catch (IOException error) { return error; }
    }
}
