using System.Windows.Threading;
using Hitbench.Resources.DataModels;
using Hitbench.Services.Dialogs;
using Hitbench.Services.FileSystem;

namespace Hitbench.Test.Presentation;

[Apartment(ApartmentState.STA), NonParallelizable]
public class OHsHitNormalDialogServiceTests
{
    [TestCase(false)] [TestCase(true)]
    public void PresenterFailureOrCancellationJoinsWaveformsBeforeDisposingReader(bool cancel)
    {
        using var cancellation = new CancellationTokenSource();
        var release = new TaskCompletionSource<Hitbench.Core.Entities.WaveformData>();
        var entered = new TaskCompletionSource();
        CancellationToken readToken = default;
        var reader = new DisposableSampleBrowserService { Waveform = (_, token) => { readToken = token; return release.Task; } };
        var service = new OHsHitNormalDialogService(() => throw new AssertionException("Native owner accessed"), Dispatcher.CurrentDispatcher,
            new OHsHitNormalSamples(new StubSampleFileService(["a.wav"])),
            (vm, token) =>
            {
                _ = vm.LoadWaveformAsync(vm.CatalogSamples.Single(s => s.Path == "a.wav"));
                entered.SetResult();
                if (!cancel) throw new IOException("presenter failed");
                cancellation.Cancel();
                return Task.FromResult(true);
            }, () => reader, new ImmediateUiScheduler());
        var task = service.RequestAsync("project.json", "", new(), cancellation.Token);
        UiTestPump.Run(entered.Task);
        try
        {
            Assert.That(readToken.IsCancellationRequested, Is.True);
            Assert.That(task.IsCompleted, Is.False);
            Assert.That(reader.DisposeCalls, Is.Zero);
        }
        finally { release.TrySetResult(new(TimeSpan.FromSeconds(1), [.1f])); }
        if (cancel) Assert.Throws<OperationCanceledException>(() => UiTestPump.Run(task));
        else Assert.Throws<IOException>(() => UiTestPump.Run(task));
        Assert.That(reader.DisposeCalls, Is.EqualTo(1));
    }

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
