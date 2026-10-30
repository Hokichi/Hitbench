using CommunityToolkit.Mvvm.Messaging;
using Hitbench.Resources.DataModels;
using Hitbench.ViewModels.Shell.HomePage;
namespace Hitbench.Test.Presentation;
public sealed class HomeSearchDebounceTests
{
    [Test]
    public async Task TypingBurstCancelsSupersededWorkBeforeFiltering()
    {
        var inventory = new FolderSnapshot(Enumerable.Range(0,10000).Select(i => new FolderEntry(i.ToString(), i.ToString(), default)));
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var vm = new HomePageVM(new StubHomeFolderService { Songs = (_, _) => Task.FromResult<FolderSnapshot?>(inventory) },
            new StubAppSettingsService(), new StubInstallationDialogService(), new ImmediateUiScheduler(),
            new StubProjectFileService((_,_,_) => Task.FromResult("project.json")), new WeakReferenceMessenger(),
            searchDelay: (_, token) => release.Task.WaitAsync(token));
        try
        {
            await vm.LoadAsync(default); await vm.LoadInitialSongsAsync(default); vm.CompleteSongsPresentation(vm.SongsPresentationGeneration);
            var pending = new List<Task>();
            for (var i = 0; i < 20; i++) { vm.SongSearchText = i.ToString(); pending.Add(vm.SearchSongsCommand.ExecutionTask!); }
            Assert.That(vm.SongSearchFilterCount, Is.Zero);
            release.SetResult(); await Task.WhenAll(pending);
            Assert.That(vm.SongSearchFilterCount, Is.EqualTo(1));
            Assert.That(vm.Songs.All(row => row.Name.Contains("19")), Is.True);
            TestContext.Out.WriteLine("Typing burst: 20 requested queries; 1 filter executed over 10,000 entries.");
        }
        finally { vm.Deactivate(); }
    }
}
