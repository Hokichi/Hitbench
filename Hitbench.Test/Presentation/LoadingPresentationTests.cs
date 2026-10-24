using Hitbench.ViewModels.Shell.HomePage;
namespace Hitbench.Test.Presentation;
public sealed class LoadingPresentationTests
{
    [Test]
    public async Task InitialMessageEndsAfterPresentationAndStaysOffForPagination()
    {
        var vm=new HomePageVM(new StubHomeFolderService(),new StubAppSettingsService(),new StubInstallationDialogService(),new StubProjectCreationDialogService(),new ImmediateUiScheduler(), new StubProjectFileService((_,_,_)=>Task.FromResult("saved")), new CommunityToolkit.Mvvm.Messaging.WeakReferenceMessenger());
        await vm.LoadAsync(default);
        Assert.That(vm.IsInitialLoading,Is.True);
        vm.CompleteSongsPresentation(vm.SongsPresentationGeneration);
        Assert.That(vm.IsInitialLoading,Is.False);
        await vm.LoadMoreProjectsCommand.ExecuteAsync(null);
        Assert.That(vm.IsInitialLoading,Is.False);
        vm.Deactivate();
    }
}
