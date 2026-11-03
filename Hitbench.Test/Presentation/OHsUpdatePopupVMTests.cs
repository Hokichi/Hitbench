using Hitbench.Test.Services;
using Hitbench.ViewModels.Popups;

namespace Hitbench.Test.Presentation;

public sealed class OHsUpdatePopupVMTests
{
    [Test]
    public async Task CancelledDownloadReturnsNothingAndHasRequestedVersionText()
    {
        var service = new StubOHsUpdateService { Stage = async (_, _, token) => { await Task.Delay(Timeout.Infinite, token); throw new AssertionException("Unreachable"); } };
        var vm = new OHsUpdatePopupVM(OHsUpdateTestData.Candidate(100), service);
        Assert.That(vm.DownloadingText, Is.EqualTo("Downloading version 1.2.4-beta"));
        var task = vm.DownloadAsync(default);
        vm.RequestClose();
        Assert.That(await task, Is.Null);
        await vm.DeactivateAsync();
    }
}
