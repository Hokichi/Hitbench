using System.Windows.Threading;
using Hitbench.Services;
using Hitbench.Services.Dialogs;
using Hitbench.ViewModels.Popups;

namespace Hitbench.Test.Presentation;

[Apartment(ApartmentState.STA)]
[NonParallelizable]
public sealed class SampleFileDialogServiceTests
{
    private readonly List<StubSamplePreviewService> previews = [];
    [SetUp]
    public void Setup() => previews.Clear();
    private SampleLoaderPopupVM Create()
    {
        var browser = new StubSampleBrowserService();
        browser.DefaultDirectory = browser.RootPath;
        browser.Entries[browser.RootPath] = [];
        var preview = new StubSamplePreviewService();
        previews.Add(preview);
        return new(browser, preview, new ImmediateUiScheduler(), new PlaybackVolumeSettings());
    }

    [Test]
    public void DialogInitializesRequestedDirectoryBeforeSelection()
    {
        var browser = new StubSampleBrowserService();
        var requested = Path.Combine(browser.RootPath, "Beatmap");
        browser.Entries[browser.RootPath] = [new(requested, "Beatmap", true)];
        browser.Entries[requested] = [];
        var dialog = new SampleFileDialogService(() => null, Dispatcher.CurrentDispatcher,
            () => new(browser, new StubSamplePreviewService(), new ImmediateUiScheduler(), new PlaybackVolumeSettings()),
            (model, _) =>
            {
                Assert.That(model.CurrentDirectory, Is.EqualTo(requested));
                Assert.That(browser.DefaultDirectoryRequests, Is.Zero);
                return Task.FromResult<IReadOnlyList<string>>([]);
            });
        UiTestPump.Run(dialog.SelectAsync(requested, default));
    }

    [Test]
    public void FreshPopupLifetimeIsInitializedAndAlwaysDeactivated()
    {
        var dialog = new SampleFileDialogService(() => null, Dispatcher.CurrentDispatcher, Create, (vm, _) =>
        {
            Assert.That(vm.CurrentDirectory, Is.Not.Empty);
            return Task.FromResult<IReadOnlyList<string>>([]);
        });
        UiTestPump.Run(dialog.SelectAsync((string?)null, default));
        UiTestPump.Run(dialog.SelectAsync((string?)null, default));
        Assert.That(previews, Has.Count.EqualTo(2));
        Assert.That(previews.All(preview => preview.Disposed), Is.True);
    }

    [Test]
    public void CancelledSelectionReturnsEmptyAndDisposesPreview()
    {
        using var cancellation = new CancellationTokenSource();
        var dialog = new SampleFileDialogService(() => null, Dispatcher.CurrentDispatcher, Create, (_, _) =>
        {
            cancellation.Cancel();
            return Task.FromResult<IReadOnlyList<string>>(["a.wav"]);
        });
        var selecting = dialog.SelectAsync((string?)null, cancellation.Token);
        UiTestPump.Run(selecting);
        Assert.That(selecting.Result, Is.Empty);
        Assert.That(previews.Single().Disposed, Is.True);
    }

    [Test]
    public void SelectionResultIsCopiedBeforeReturn()
    {
        List<string> paths = ["a.wav"];
        var dialog = new SampleFileDialogService(() => null, Dispatcher.CurrentDispatcher, Create,
            (_, _) => Task.FromResult<IReadOnlyList<string>>(paths));
        var selecting = dialog.SelectAsync((string?)null, default);
        UiTestPump.Run(selecting);
        paths.Clear();
        Assert.That(selecting.Result, Is.EqualTo(new[] { "a.wav" }));
    }
}
