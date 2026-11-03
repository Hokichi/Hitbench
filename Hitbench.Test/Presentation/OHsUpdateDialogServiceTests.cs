using System.Windows.Threading;
using Hitbench.Services.Dialogs;
using Hitbench.Test.Services;
using Hitbench.ViewModels.Popups;

namespace Hitbench.Test.Presentation;

[Apartment(ApartmentState.STA), NonParallelizable]
public sealed class OHsUpdateDialogServiceTests
{
    [Test]
    public void NonDisplayingPresentationReturnsStagedResult()
    {
        var staged = new Hitbench.Resources.DataModels.OHsStagedUpdate("1.2.4-beta", "unused", "unused", []);
        var update = new StubOHsUpdateService { Stage = (_, _, _) => Task.FromResult(staged) };
        var service = new OHsUpdateDialogService(() => null, Dispatcher.CurrentDispatcher, release => new OHsUpdatePopupVM(release, update),
            (vm, token) => vm.DownloadAsync(token));
        var showing = service.ShowAsync(OHsUpdateTestData.Candidate(100), default);
        UiTestPump.Run(showing);
        Assert.That(showing.Result, Is.SameAs(staged));
    }
}
