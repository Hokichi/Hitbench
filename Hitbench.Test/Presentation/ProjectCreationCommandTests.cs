using CommunityToolkit.Mvvm.Messaging;
using Hitbench.Resources.DataModels;
using Hitbench.Resources.Messages;
using Hitbench.ViewModels.Shell.HomePage;

namespace Hitbench.Test.Presentation;

public class ProjectCreationCommandTests
{
    private readonly FolderEntry song = new(@"C:\Songs\Selected", "Selected", DateTimeOffset.MinValue)
    {
        DisplayName = "Artist - Title"
    };
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
    public async Task SuppliesSuggestionAndNamesThenWaitsForSaveAndRejectsOverlap()
    {
        var pending = new TaskCompletionSource<string>();
        var calls = 0;
        var existing = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Existing" };
        var dialog = new StubProjectCreationDialogService { Result = "Manual project" };
        var vm = CreateVM((folder, projectName, _) =>
        {
            Assert.That(folder.FullName, Is.EqualTo(song.FullName));
            Assert.That(projectName, Is.EqualTo("Manual project"));
            calls++;
            return pending.Task;
        }, dialog, existing);
        vm.SongsLoadStatus = "Ready";

        var first = vm.CreateProjectCommand.ExecuteAsync(song);

        Assert.Multiple(() =>
        {
            Assert.That(dialog.SuggestedName, Is.EqualTo(song.DisplayName));
            Assert.That(dialog.ExistingNames, Is.SameAs(existing));
            Assert.That(dialog.ExistingNames!.Contains("EXISTING"), Is.True);
            Assert.That(vm.IsCreatingProject, Is.True);
            Assert.That(vm.CreateProjectCommand.CanExecute(song), Is.False);
            Assert.That(created, Is.Empty);
            Assert.That(failures, Is.Empty);
            Assert.That(calls, Is.EqualTo(1));
        });
        pending.SetResult(@"C:\App\projects\Manual project\project.json");
        await first;
        Assert.That(created, Is.EqualTo(new[] { @"C:\App\projects\Manual project\project.json" }));
        Assert.That(vm.SongsStatus, Is.EqualTo("Ready"));
        Assert.That(vm.IsCreatingProject, Is.False);
    }

    [TestCase(null)]
    [TestCase("")]
    public async Task CancelledOrEmptyDialogResultDoesNotPersist(string? result)
    {
        var dialog = new StubProjectCreationDialogService { Result = result };
        var vm = CreateVM((_, _, _) => throw new AssertionException("Persistence must not run"), dialog);

        await vm.CreateProjectCommand.ExecuteAsync(song);

        Assert.That(dialog.CallCount, Is.EqualTo(1));
        Assert.That(created, Is.Empty);
        Assert.That(failures, Is.Empty);
        Assert.That(vm.IsCreatingProject, Is.False);
    }

    [Test]
    public async Task PersistenceFailureDoesNotNavigateAndAllowsRetry()
    {
        var fail = true;
        var dialog = new StubProjectCreationDialogService { Result = "Manual" };
        var vm = CreateVM((_, _, _) => fail
            ? throw new IOException("Disk unavailable")
            : Task.FromResult("saved.json"), dialog);

        await vm.CreateProjectCommand.ExecuteAsync(song);

        Assert.That(created, Is.Empty);
        Assert.That(failures.Single(), Does.Contain("Disk unavailable"));
        Assert.That(vm.FatalError, Is.Null);
        Assert.That(vm.CreateProjectCommand.CanExecute(song), Is.True);
        fail = false;
        await vm.CreateProjectCommand.ExecuteAsync(song);
        Assert.That(created, Is.EqualTo(new[] { "saved.json" }));
    }

    [Test]
    public async Task CancellationDoesNotPublishSuccessOrFailure()
    {
        var pending = new TaskCompletionSource<string>();
        var dialog = new StubProjectCreationDialogService { Result = "Manual" };
        var vm = CreateVM((_, _, token) => pending.Task.WaitAsync(token), dialog);
        var operation = vm.CreateProjectCommand.ExecuteAsync(song);

        vm.CreateProjectCommand.Cancel();
        await operation;

        Assert.That(created, Is.Empty);
        Assert.That(failures, Is.Empty);
        Assert.That(vm.IsCreatingProject, Is.False);
    }

    [Test]
    public async Task NullSelectionAndLoadingCannotCreate()
    {
        var dialog = new StubProjectCreationDialogService { Result = "Manual" };
        var vm = CreateVM((_, _, _) => throw new AssertionException("Creation must not run"), dialog);
        Assert.That(vm.CreateProjectCommand.CanExecute(null), Is.False);
        await vm.CreateProjectCommand.ExecuteAsync(null);
        vm.IsInteractionBlocked = true;
        Assert.That(vm.CreateProjectCommand.CanExecute(song), Is.False);
        await vm.CreateProjectCommand.ExecuteAsync(song);
        Assert.That(dialog.CallCount, Is.Zero);
    }

    [Test]
    public async Task DeactivatedHomeDoesNotNavigateAfterCommittedCreation()
    {
        var completion = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var vm = CreateVM((_, _, _) => completion.Task, new StubProjectCreationDialogService { Result = "Manual" });
        var operation = vm.CreateProjectCommand.ExecuteAsync(song);
        vm.Deactivate();
        completion.SetResult("committed.json");
        await operation;
        Assert.That(created, Is.Empty);
        Assert.That(failures, Is.Empty);
    }

    private HomePageVM CreateVM(
        Func<DirectoryInfo, string, CancellationToken, Task<string>> create,
        StubProjectCreationDialogService dialog,
        IReadOnlySet<string>? existingNames = null) =>
        new(new StubHomeFolderService(_ => Task.FromResult(new HomeFolderData([], [], @"C:\osu", true, false))),
            new StubAppSettingsService(), new StubInstallationDialogService(), dialog,
            new ImmediateUiScheduler(), new StubProjectFileService(create, existingNames), messenger);
}
