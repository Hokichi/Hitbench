using CommunityToolkit.Mvvm.Messaging;
using Hitbench.Resources.Messages;

namespace Hitbench.Test.Presentation;

public sealed class ProjectCreationCommandTests
{
    [Test]
    public async Task PersistenceFailureDoesNotNavigateAndAllowsRetry()
    {
        var fail = true;
        var messenger = new WeakReferenceMessenger();
        var created = new List<string>();
        var failures = new List<string>();
        messenger.Register<ProjectCreatedMessage>(created, (_, m) => created.Add(m.ProjectPath));
        messenger.Register<ProjectCreationFailedMessage>(failures, (_, m) => failures.Add(m.Error));
        var vm = HomeInlineProjectCreationTests.Create((_, _, _) => fail
            ? throw new IOException("Disk unavailable") : Task.FromResult("saved.json"), messenger);
        try
        {
            vm.SelectedMapset = HomeInlineProjectCreationTests.Mapset;
            await vm.ShowProjectCreationCommand.ExecuteAsync(null);
            vm.DraftProjectName = "Manual";
            await vm.CreateProjectCommand.ExecuteAsync(null);
            Assert.That(created, Is.Empty);
            Assert.That(failures.Single(), Does.Contain("Disk unavailable"));
            Assert.That(vm.DraftProjectName, Is.EqualTo("Manual"));
            Assert.That(vm.CreateProjectCommand.CanExecute(null), Is.True);
            fail = false;
            await vm.CreateProjectCommand.ExecuteAsync(null);
            Assert.That(created, Is.EqualTo(new[] { "saved.json" }));
        }
        finally { vm.Deactivate(); }
    }

    [TestCase(false)] [TestCase(true)]
    public async Task CancellationOrDeactivationSuppressesLateNavigation(bool deactivate)
    {
        var pending = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var messenger = new WeakReferenceMessenger();
        var created = new List<string>();
        messenger.Register<ProjectCreatedMessage>(created, (_, m) => created.Add(m.ProjectPath));
        var vm = HomeInlineProjectCreationTests.Create((_, _, _) => pending.Task, messenger);
        try
        {
            vm.SelectedMapset = HomeInlineProjectCreationTests.Mapset;
            await vm.ShowProjectCreationCommand.ExecuteAsync(null);
            vm.DraftProjectName = "Manual";
            var creating = vm.CreateProjectCommand.ExecuteAsync(null);
            if (deactivate) vm.Deactivate(); else vm.CreateProjectCommand.Cancel();
            pending.SetResult("committed.json");
            await creating;
            Assert.That(created, Is.Empty);
            Assert.That(vm.IsCreatingProject, Is.False);
        }
        finally { pending.TrySetResult("committed.json"); vm.Deactivate(); }
    }
}
