using CommunityToolkit.Mvvm.Messaging;
using osuHitsounder.Resources.DataModels;
using osuHitsounder.Resources.Messages;
using osuHitsounder.ViewModels.Shell.HomePage;

namespace osuHitsounder.Test.Presentation;

public class ProjectCreationCommandTests
{
    private readonly DirectoryInfo song = new("C:\\Songs\\Selected");
    private readonly List<string> created = [];
    private readonly List<string> failures = [];
    private WeakReferenceMessenger messenger = null!;

    [SetUp]
    public void SetUp()
    {
        created.Clear();
        failures.Clear();
        messenger = new WeakReferenceMessenger();
        messenger.Register<ProjectCreatedMessage>(this, (_, message) => created.Add(message.ProjectPath));
        messenger.Register<ProjectCreationFailedMessage>(this, (_, message) => failures.Add(message.Error));
    }

    [Test]
    public async Task WaitsForSaveAndRejectsOverlappingAttempts()
    {
        var pending = new TaskCompletionSource<string>();
        var calls = 0;
        var vm = CreateVM((folder, projectName, _) =>
        {
            Assert.That(folder, Is.SameAs(song));
            Assert.That(projectName, Is.EqualTo(song.Name));
            calls++;
            return pending.Task;
        });
        vm.SongsLoadStatus = "Ready";
        var first = vm.CreateProjectCommand.ExecuteAsync(song);
        Assert.That(vm.IsCreatingProject, Is.True);
        Assert.That(vm.CreateProjectCommand.CanExecute(song), Is.False);
        Assert.That(created, Is.Empty);
        Assert.That(failures, Is.Empty);
        if (vm.CreateProjectCommand.CanExecute(song))
            await vm.CreateProjectCommand.ExecuteAsync(song);
        Assert.That(calls, Is.EqualTo(1));
        pending.SetResult("C:\\App\\projects\\Selected\\project.json");
        await first;
        Assert.That(created, Is.EqualTo(new[] { "C:\\App\\projects\\Selected\\project.json" }));
        Assert.That(vm.SongsStatus, Is.EqualTo("Ready"));
        Assert.That(vm.IsCreatingProject, Is.False);
    }

    [Test]
    public async Task FailureDoesNotNavigateAndAllowsExplicitRetry()
    {
        var fail = true;
        var vm = CreateVM((_, _, _) => fail ? throw new IOException("Disk unavailable") : Task.FromResult("saved.json"));
        await vm.CreateProjectCommand.ExecuteAsync(song);
        Assert.That(created, Is.Empty);
        Assert.That(failures.Single(), Does.Contain("Disk unavailable"));
        Assert.That(vm.FatalError, Is.Null);
        Assert.That(vm.IsCreatingProject, Is.False);
        Assert.That(vm.CreateProjectCommand.CanExecute(song), Is.True);
        fail = false;
        await vm.CreateProjectCommand.ExecuteAsync(song);
        Assert.That(created, Is.EqualTo(new[] { "saved.json" }));
        Assert.That(failures, Has.Count.EqualTo(1));
    }

    [Test]
    public async Task CancellationDoesNotPublishSuccessOrFailure()
    {
        var pending = new TaskCompletionSource<string>();
        var vm = CreateVM((_, _, token) => pending.Task.WaitAsync(token));
        var operation = vm.CreateProjectCommand.ExecuteAsync(song);
        vm.CreateProjectCommand.Cancel();
        await operation;
        Assert.That(created, Is.Empty);
        Assert.That(failures, Is.Empty);
        Assert.That(vm.IsCreatingProject, Is.False);
    }

    [Test]
    public async Task CancelledOperationIgnoringTokenDoesNotNavigate()
    {
        var pending = new TaskCompletionSource<string>();
        var vm = CreateVM((_, _, _) => pending.Task);
        var operation = vm.CreateProjectCommand.ExecuteAsync(song);
        vm.CreateProjectCommand.Cancel();
        pending.SetResult("saved.json");
        await operation;
        Assert.That(created, Is.Empty);
        Assert.That(failures, Is.Empty);
    }

    [Test]
    public async Task NullSelectionAndLoadingCannotCreate()
    {
        var vm = CreateVM((_, _, _) => throw new AssertionException("Creation must not run"));
        Assert.That(vm.CreateProjectCommand.CanExecute(null), Is.False);
        await vm.CreateProjectCommand.ExecuteAsync(null);
        vm.IsInteractionBlocked = true;
        Assert.That(vm.CreateProjectCommand.CanExecute(song), Is.False);
        await vm.CreateProjectCommand.ExecuteAsync(song);
        Assert.That(created, Is.Empty);
        Assert.That(failures, Is.Empty);
    }

    private HomePageVM CreateVM(Func<DirectoryInfo, string, CancellationToken, Task<string>> create) =>
        new(new StubHomeFolderService(_ => Task.FromResult(new HomeFolderData([], [], "C:\\osu", true, false))),
            new StubAppSettingsService(), new StubInstallationDialogService(), new ImmediateUiScheduler(), new StubProjectFileService(create), messenger);
}

