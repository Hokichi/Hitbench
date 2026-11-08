using System.Windows.Threading;
using Hitbench.Services.Dialogs;
namespace Hitbench.Test.Presentation;
[Apartment(ApartmentState.STA), NonParallelizable]
public sealed class OHsMapsetSampleImportDialogServiceTests
{
    [TestCase(false)] [TestCase(true)]
    public void PresenterOnlyReturnsCheckedPaths(bool accept)
    {
        var service=new OHsMapsetSampleImportDialogService(()=>throw new AssertionException("Native owner accessed"),Dispatcher.CurrentDispatcher,
            (vm,_)=>{vm.Entries[0].IsChecked=false;return Task.FromResult(accept);});
        var task=service.SelectAsync(["drum-hitnormal9.wav","soft-hitclap.wav"],default);UiTestPump.Run(task);
        Assert.That(task.Result,Is.EqualTo(accept?new[]{"soft-hitclap.wav"}:Array.Empty<string>()));
    }
    [Test]
    public void CancellationAfterPresenterAcceptanceDoesNotImport()
    {
        using var cancellation=new CancellationTokenSource();
        var service=new OHsMapsetSampleImportDialogService(()=>throw new AssertionException("Native owner accessed"),Dispatcher.CurrentDispatcher,
            (_,_)=>{cancellation.Cancel();return Task.FromResult(true);});
        var task=service.SelectAsync(["drum-hitnormal9.wav"],cancellation.Token);UiTestPump.Run(task);
        Assert.That(task.Result,Is.Empty);
    }
    [Test]
    public void PresentationFailureClosesDraftAndPreservesException()
    {
        Hitbench.ViewModels.Popups.OHsMapsetSampleImportPopupVM? draft=null;
        var error=new IOException("present failed");
        var service=new OHsMapsetSampleImportDialogService(()=>throw new AssertionException("Native owner accessed"),Dispatcher.CurrentDispatcher,
            (vm,_)=>{draft=vm;return Task.FromException<bool>(error);});
        Assert.That(Assert.Throws<IOException>(()=>UiTestPump.Run(service.SelectAsync(["drum-hitnormal9.wav"],default))),Is.SameAs(error));
        Assert.That(draft!.CanConfirm,Is.False);
    }
}
