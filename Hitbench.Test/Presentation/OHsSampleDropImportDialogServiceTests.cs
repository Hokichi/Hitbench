using System.Windows.Threading;
using Hitbench.Services.Dialogs;
using Hitbench.ViewModels.Popups;
namespace Hitbench.Test.Presentation;

[Apartment(ApartmentState.STA), NonParallelizable]
public sealed class OHsSampleDropImportDialogServiceTests
{
    [Test]
    public void PrimaryErrorSurvivesCleanupFailure()
    {
        var primary = new IOException("present failed");
        var preview = new StubSamplePreviewService { Dispose = () => Task.FromException(new IOException("cleanup failed")) };
        var service = new OHsSampleDropImportDialogService(() => throw new AssertionException("Native owner requested"), Dispatcher.CurrentDispatcher,
            paths => new(paths, new StubSampleBrowserService(), preview, new ImmediateUiScheduler()), (_, _) => Task.FromException<bool>(primary));
        Assert.That(Assert.Throws<IOException>(() => UiTestPump.Run(service.SelectAsync(new[] { "a.wav" }, default))), Is.SameAs(primary));
    }

    [Test]
    public void CancellationAfterAcceptanceReturnsNoPaths()
    {
        using var cancellation = new CancellationTokenSource();
        var preview = new StubSamplePreviewService();
        preview.Dispose = () => { cancellation.Cancel(); return Task.CompletedTask; };
        var service = new OHsSampleDropImportDialogService(() => throw new AssertionException("Native owner requested"), Dispatcher.CurrentDispatcher,
            paths => new(paths, new StubSampleBrowserService(), preview, new ImmediateUiScheduler()), (_, _) => Task.FromResult(true));
        var operation = service.SelectAsync(new[] { "a.wav" }, cancellation.Token);
        UiTestPump.Run(operation);
        Assert.That(operation.Result, Is.Empty);
    }
    [TestCase(false)]
    [TestCase(true)]
    public void PresenterReturnsOnlyCheckedPathsAndAlwaysDisposes(bool accept)
    {
        var preview = new StubSamplePreviewService(); var service = new OHsSampleDropImportDialogService(() => throw new AssertionException("Native owner requested"), Dispatcher.CurrentDispatcher, paths => new OHsSampleDropImportPopupVM(paths, new StubSampleBrowserService(), preview, new ImmediateUiScheduler()), (vm, _) => { vm.Entries[0].IsChecked = false; return Task.FromResult(accept); });
        var operation = service.SelectAsync(new[] { "a.wav", "b.ogg" }, default); UiTestPump.Run(operation); Assert.That(operation.Result, Is.EqualTo(accept ? new[] { "b.ogg" } : Array.Empty<string>())); Assert.That(preview.Disposed, Is.True);
    }
}
