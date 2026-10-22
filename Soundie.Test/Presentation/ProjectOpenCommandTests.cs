using CommunityToolkit.Mvvm.Messaging;
using Soundie.Resources.DataModels;
using Soundie.Resources.Messages;
using Soundie.ViewModels.Shell.HomePage;

namespace Soundie.Test.Presentation;

public class ProjectOpenCommandTests
{
    private readonly FolderEntry project = new(
        @"C:\App\projects\Existing", "Existing", DateTimeOffset.MinValue);

    [Test]
    public void OpenProjectPublishesSelectedProjectFile()
    {
        var opened = new List<string>();
        var messenger = new WeakReferenceMessenger();
        messenger.Register<ProjectOpenRequestedMessage>(this,
            (_, message) => opened.Add(message.ProjectPath));
        var vm = CreateVM(messenger);

        vm.OpenProjectCommand.Execute(project);

        Assert.Multiple(() =>
        {
            Assert.That(opened, Is.EqualTo(new[] { @"C:\App\projects\Existing\project.json" }));
            Assert.That(vm.OpenProjectCommand.CanExecute(null), Is.False);
        });
    }

    [TestCase(nameof(HomePageVM.IsInteractionBlocked))]
    [TestCase(nameof(HomePageVM.IsCreatingProject))]
    [TestCase(nameof(HomePageVM.RequiresShutdown))]
    [TestCase(nameof(HomePageVM.FatalError))]
    [TestCase("Deactivated")]
    public void UnavailableHomePageDoesNotOpenProject(string state)
    {
        var opened = new List<string>();
        var messenger = new WeakReferenceMessenger();
        messenger.Register<ProjectOpenRequestedMessage>(this,
            (_, message) => opened.Add(message.ProjectPath));
        var vm = CreateVM(messenger);
        switch (state)
        {
            case nameof(HomePageVM.IsInteractionBlocked): vm.IsInteractionBlocked = true; break;
            case nameof(HomePageVM.IsCreatingProject): vm.IsCreatingProject = true; break;
            case nameof(HomePageVM.RequiresShutdown): vm.RequiresShutdown = true; break;
            case nameof(HomePageVM.FatalError): vm.FatalError = "Unavailable"; break;
            default: vm.Deactivate(); break;
        }

        vm.OpenProjectCommand.Execute(project);

        Assert.Multiple(() =>
        {
            Assert.That(vm.OpenProjectCommand.CanExecute(project), Is.False);
            Assert.That(opened, Is.Empty);
        });
    }

    [Test]
    public void BlockingStateInvalidatesOpenProjectCommand()
    {
        var vm = CreateVM(new WeakReferenceMessenger());
        var changes = 0;
        vm.OpenProjectCommand.CanExecuteChanged += (_, _) => changes++;

        vm.IsInteractionBlocked = true;
        vm.IsCreatingProject = true;

        Assert.That(changes, Is.EqualTo(2));
    }

    private static HomePageVM CreateVM(IMessenger messenger) =>
        new(new StubHomeFolderService(), new StubAppSettingsService(),
            new StubInstallationDialogService(), new StubProjectCreationDialogService(),
            new ImmediateUiScheduler(),
            new StubProjectFileService((_, _, _) =>
                throw new AssertionException("Project opening must not create a project.")), messenger);
}
