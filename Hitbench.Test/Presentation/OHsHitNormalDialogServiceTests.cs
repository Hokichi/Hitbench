using System.Windows.Threading;
using Hitbench.Resources.DataModels;
using Hitbench.Services.Dialogs;
using Hitbench.Services.FileSystem;

namespace Hitbench.Test.Presentation;

[Apartment(ApartmentState.STA), NonParallelizable]
public class OHsHitNormalDialogServiceTests
{
    [TestCase(false)][TestCase(true)]
    public void InjectedPresenterCommitsOnlyAcceptedDraft(bool accept)
    {
        var service = new OHsHitNormalDialogService(() => throw new AssertionException("Native owner accessed"), Dispatcher.CurrentDispatcher,
            new OHsHitNormalSamples(new StubSampleFileService(["a.wav"])),
            (vm, _) => { vm.NormalPath = "a.wav"; return Task.FromResult(accept); });
        var task = service.RequestAsync("project.json", "", new(), default); UiTestPump.Run(task);
        Assert.That(task.Result, Is.EqualTo(accept ? new OHsHitNormalConfiguration("a.wav") : null));
    }
}
