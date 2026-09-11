using osuHitsounder.Resources.DataModels;
using osuHitsounder.ViewModels.Shell.HomePage;

namespace osuHitsounder.Test.Presentation;

public class HomePageVMTests
{
    [TestCase(false)]
    [TestCase(true)]
    public async Task MissingOsuProducesFatalMessage(bool skipped)
    {
        var vm = new HomePageVM(new StubHomeFolderService(_ => Task.FromResult(
            new HomeFolderData([], [], null, false, skipped))));
        await vm.LoadAsync(CancellationToken.None);
        Assert.That(vm.FatalError, Does.StartWith("osu! folder wasn't found."));
        Assert.That(vm.FatalError!.Contains("Some locations"), Is.EqualTo(skipped));
        Assert.That(vm.IsLoading, Is.False);
    }

    [TestCase(true, "No song folders found.")]
    [TestCase(false, "Songs folder wasn't found.")]
    public async Task EmptySongsDistinguishesMissingFolder(bool exists, string status)
    {
        var vm = new HomePageVM(new StubHomeFolderService(_ => Task.FromResult(
            new HomeFolderData([], [], "C:\\game", exists, false))));
        await vm.LoadAsync(CancellationToken.None);
        Assert.That(vm.FatalError, Is.Null);
        Assert.That(vm.ProjectsStatus, Is.Not.Empty);
        Assert.That(vm.SongsStatus, Is.EqualTo(status));
    }

    [Test]
    public async Task PublishesLoadedCollectionsAndClearsStatus()
    {
        var vm = new HomePageVM(new StubHomeFolderService(_ => Task.FromResult(
            new HomeFolderData([new DirectoryInfo("C:\\project")], [new DirectoryInfo("C:\\song")], "C:\\game", true, false))));
        await vm.LoadAsync(CancellationToken.None);
        Assert.That(vm.Projects.Single().Name, Is.EqualTo("project"));
        Assert.That(vm.Songs.Single().Name, Is.EqualTo("song"));
        Assert.That(vm.ProjectsStatus, Is.Empty);
        Assert.That(vm.SongsStatus, Is.Empty);
    }

    [Test]
    public async Task PendingLoadIsVisibleAndCancellationRestoresState()
    {
        var result = new TaskCompletionSource<HomeFolderData>();
        using var cancellation = new CancellationTokenSource();
        var vm = new HomePageVM(new StubHomeFolderService(token => result.Task.WaitAsync(token)));
        var load = vm.LoadAsync(cancellation.Token);
        Assert.That(vm.IsLoading, Is.True);
        Assert.That(vm.SongsStatus, Is.Not.Empty);
        cancellation.Cancel();
        try { await load; Assert.Fail("Load should be cancelled."); }
        catch (OperationCanceledException) { }
        Assert.That(vm.IsLoading, Is.False);
        Assert.That(vm.FatalError, Is.Null);
    }

    [Test]
    public async Task IoFailureExplainsStartupFailure()
    {
        var vm = new HomePageVM(new StubHomeFolderService(_ => throw new IOException("Disk unavailable")));
        await vm.LoadAsync(CancellationToken.None);
        Assert.That(vm.FatalError, Does.Contain("Disk unavailable"));
        Assert.That(vm.IsLoading, Is.False);
    }
}
