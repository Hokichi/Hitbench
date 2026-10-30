using System.Windows.Threading;
using Hitbench.Services.Dialogs;
using Hitbench.ViewModels.Popups;
namespace Hitbench.Test.Presentation;

[Apartment(ApartmentState.STA), NonParallelizable]
public sealed class OHsSampleDropImportDialogServiceTests
{
    [TestCase(false)]
    [TestCase(true)]
    public void PresenterReturnsOnlyCheckedPathsAndAlwaysDisposes(bool accept)
    {
        var preview = new StubSamplePreviewService(); var service = new OHsSampleDropImportDialogService(() => throw new AssertionException("Native owner requested"), Dispatcher.CurrentDispatcher, paths => new OHsSampleDropImportPopupVM(paths, new StubSampleBrowserService(), preview, new ImmediateUiScheduler()), (vm, _) => { vm.Entries[0].IsChecked = false; return Task.FromResult(accept); });
        var operation = service.SelectAsync(new[] { "a.wav", "b.ogg" }, default); UiTestPump.Run(operation); Assert.That(operation.Result, Is.EqualTo(accept ? new[] { "b.ogg" } : Array.Empty<string>())); Assert.That(preview.Disposed, Is.True);
    }
}
