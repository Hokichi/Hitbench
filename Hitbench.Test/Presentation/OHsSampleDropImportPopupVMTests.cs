using Hitbench.ViewModels.Popups;
namespace Hitbench.Test.Presentation;

public sealed class OHsSampleDropImportPopupVMTests
{
    [Test]
    public async Task SwitchingPreviewLeavesOneActiveEntry()
    {
        var snapshot = new TaskCompletionSource<Hitbench.Services.SamplePreviewSnapshot>(TaskCreationOptions.RunContinuationsAsynchronously);
        var snapshotRequested = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var preview = new StubSamplePreviewService
        {
            Snapshot = token => { snapshotRequested.TrySetResult(); return snapshot.Task.WaitAsync(token); }
        };
        var vm = new OHsSampleDropImportPopupVM(["C:/a.wav", "C:/b.wav"], new StubSampleBrowserService(), preview, new ImmediateUiScheduler());
        await vm.InitializeAsync(default);
        try
        {
            await vm.PlayCommand.ExecuteAsync(vm.Entries[0]);
            Assert.That(vm.Entries[0].IsPlaying, Is.True);
            await vm.PlayCommand.ExecuteAsync(vm.Entries[1]);
            Assert.That(vm.Entries[0].IsPlaying, Is.False);
            Assert.That(vm.Entries[1].IsPlaying, Is.True);
            Assert.That(preview.PlayedPaths, Is.EqualTo(new[] { "C:/a.wav", "C:/b.wav" }));
            await vm.PlayCommand.ExecuteAsync(vm.Entries[1]);
            Assert.That(vm.Entries.All(entry => !entry.IsPlaying), Is.True);
            Assert.That(preview.Playing, Is.False);

            await vm.PlayCommand.ExecuteAsync(vm.Entries[1]);
            vm.Entries[1].PropertyChanged += (_, args) =>
            {
                if (args.PropertyName == nameof(Hitbench.Resources.DataModels.OHsDroppedSampleEntry.IsPlaying) && !vm.Entries[1].IsPlaying)
                    finished.TrySetResult();
            };
            await snapshotRequested.Task.WaitAsync(TimeSpan.FromSeconds(5));
            snapshot.TrySetResult(new(false, null, null));
            await finished.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.That(vm.Entries.All(entry => !entry.IsPlaying), Is.True);
            Assert.That(vm.Entries.All(entry => entry.IsChecked), Is.True);
        }
        finally
        {
            snapshot.TrySetResult(new(false, null, null));
            await vm.DeactivateAsync();
        }
    }

    [Test]
    public async Task ReentrantDeactivateUsesThePublishedCleanupTask()
    {
        var preview = new StubSamplePreviewService();
        var vm = new OHsSampleDropImportPopupVM(new[] { "a.wav" }, new StubSampleBrowserService(), preview, new ImmediateUiScheduler());
        await vm.InitializeAsync(default);
        Task? nested = null;
        vm.PropertyChanged += (_, args) => { if (args.PropertyName == nameof(vm.CanConfirm)) nested = vm.DeactivateAsync(); };
        var close = vm.DeactivateAsync(); await close;
        Assert.That(nested, Is.SameAs(close));
        Assert.That(preview.DisposeCalls, Is.EqualTo(1));
    }
    [Test]
    public async Task WaveformQueueCancelsWithoutLateMutation()
    {
        var gate = new TaskCompletionSource<Hitbench.Core.Entities.WaveformData>(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        var vm = new OHsSampleDropImportPopupVM(new[] { "a.wav", "b.ogg", "c.mp3" }, new StubSampleBrowserService { Waveform = (_, _) => { calls++; return gate.Task; } }, new StubSamplePreviewService(), new ImmediateUiScheduler());
        await vm.InitializeAsync(default);
        var reads = vm.Entries.Select(vm.LoadWaveformAsync).ToArray();
        Assert.That(calls, Is.EqualTo(2));
        var close = vm.DeactivateAsync();
        gate.TrySetResult(new(TimeSpan.FromSeconds(1), new float[] { .5f }));
        await close; await Task.WhenAll(reads);
        Assert.That(calls, Is.EqualTo(2));
        Assert.That(vm.Entries.All(e => e.Waveform is null && e.IsChecked), Is.True);
    }

    [Test]
    public async Task PreviewAndReadFailuresPreserveCheckedEntries()
    {
        var vm = new OHsSampleDropImportPopupVM(new[] { "a.wav" }, new StubSampleBrowserService { Waveform = (_, _) => throw new IOException("read failed") }, new StubSamplePreviewService { Play = _ => throw new IOException("play failed") }, new ImmediateUiScheduler());
        await vm.InitializeAsync(default);
        await vm.LoadWaveformAsync(vm.Entries[0]); await vm.PlayCommand.ExecuteAsync(vm.Entries[0]);
        Assert.That(vm.Entries, Has.Count.EqualTo(1));
        Assert.That(vm.Entries[0].IsChecked, Is.True);
        Assert.That(vm.Entries[0].IsPlaying, Is.False);
        await vm.DeactivateAsync();
    }
    [TestCase(true)]
    [TestCase(false)]
    public async Task FirstSelectionOutcomeFreezesCheckedPaths(bool accept)
    {
        var vm = new OHsSampleDropImportPopupVM(new[] { "a.wav", "b.ogg" }, new StubSampleBrowserService(), new StubSamplePreviewService(), new ImmediateUiScheduler());
        await vm.InitializeAsync(default);
        vm.Entries[1].IsChecked = false;
        Assert.That(vm.TryCompleteSelection(accept), Is.True);
        vm.Entries[0].IsChecked = false; vm.Entries[1].IsChecked = true;
        Assert.That(vm.TryCompleteSelection(!accept), Is.False);
        Assert.That(vm.SelectionAccepted, Is.EqualTo(accept));
        Assert.That(vm.SelectedPaths, Is.EqualTo(accept ? new[] { "a.wav" } : Array.Empty<string>()));
        Assert.That(vm.PlayCommand.CanExecute(vm.Entries[0]), Is.False);
        await vm.DeactivateAsync();
        Assert.That(vm.SelectedPaths, Is.EqualTo(accept ? new[] { "a.wav" } : Array.Empty<string>()));
    }

    [Test]
    public async Task EmptySelectionCannotCompleteAsAccepted()
    {
        var vm = new OHsSampleDropImportPopupVM(new[] { "a.wav" }, new StubSampleBrowserService(), new StubSamplePreviewService(), new ImmediateUiScheduler());
        await vm.InitializeAsync(default); vm.Entries[0].IsChecked = false;
        Assert.That(vm.TryCompleteSelection(true), Is.False);
        Assert.That(vm.SelectionAccepted, Is.Null);
        Assert.That(vm.TryCompleteSelection(false), Is.True);
        await vm.DeactivateAsync();
    }
    [Test]
    public async Task ConcurrentDeactivateAwaitsOneCleanup()
    {
        var gate = new TaskCompletionSource<Hitbench.Core.Entities.WaveformData>(TaskCreationOptions.RunContinuationsAsynchronously);
        var preview = new StubSamplePreviewService();
        var vm = new OHsSampleDropImportPopupVM(new[] { "a.wav" }, new StubSampleBrowserService { Waveform = (_, _) => gate.Task }, preview, new ImmediateUiScheduler());
        await vm.InitializeAsync(default);
        var read = vm.LoadWaveformAsync(vm.Entries[0]);
        var first = vm.DeactivateAsync();
        var second = vm.DeactivateAsync();
        try { Assert.That(second, Is.SameAs(first)); Assert.That(second.IsCompleted, Is.False); }
        finally { gate.TrySetResult(new(TimeSpan.FromSeconds(1), new float[] { .5f })); await first; await second; await read; }
        Assert.That(preview.DisposeCalls, Is.EqualTo(1));
    }

    [Test]
    public async Task CleanupFailureStillReleasesRemainingResources()
    {
        var error = new IOException("preview dispose failed");
        var browser = new DisposableSampleBrowserService();
        var vm = new OHsSampleDropImportPopupVM(new[] { "a.wav" }, browser, new StubSamplePreviewService { Dispose = () => Task.FromException(error) }, new ImmediateUiScheduler());
        await vm.InitializeAsync(default);
        Assert.That(Assert.ThrowsAsync<IOException>(() => vm.DeactivateAsync()), Is.SameAs(error));
        Assert.That(browser.DisposeCalls, Is.EqualTo(1));
    }

    [Test]
    public async Task CancelDuringPreviewStartStopsLatePlayback()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var preview = new StubSamplePreviewService { IgnorePlayCancellation = true, Play = _ => { entered.TrySetResult(); return gate.Task; } };
        var vm = new OHsSampleDropImportPopupVM(new[] { "a.wav" }, new StubSampleBrowserService(), preview, new ImmediateUiScheduler());
        await vm.InitializeAsync(default);
        var play = vm.PlayCommand.ExecuteAsync(vm.Entries[0]); await entered.Task;
        var close = vm.DeactivateAsync();
        try { Assert.That(vm.Entries[0].IsPlaying, Is.False); Assert.That(close.IsCompleted, Is.False); }
        finally { gate.TrySetResult(); await close; await play; }
        Assert.That(preview.Playing, Is.False);
        Assert.That(preview.DisposeCalls, Is.EqualTo(1));
    }
    [Test]
    public async Task CheckedSelectionAndExternalPreviewAreOwned()
    {
        var preview = new StubSamplePreviewService();
        var vm = new OHsSampleDropImportPopupVM(new[] { "C:/one/a.wav", "C:/two/b.ogg" }, new StubSampleBrowserService(), preview, new ImmediateUiScheduler());
        await vm.InitializeAsync(default);
        vm.Entries[1].IsChecked = false;
        Assert.That(vm.GetCheckedPaths(), Is.EqualTo(new[] { "C:/one/a.wav" }));
        await vm.PlayCommand.ExecuteAsync(vm.Entries[0]);
        Assert.That(preview.PlayedPaths.Single(), Is.EqualTo("C:/one/a.wav"));
        vm.Entries[0].IsChecked = false; Assert.That(vm.CanConfirm, Is.False);
        await vm.DeactivateAsync(); Assert.That(preview.Disposed, Is.True);
    }
 [Test] public async Task LateWaveformIsIgnoredAfterDeactivation()
 {
  var gate=new TaskCompletionSource<Hitbench.Core.Entities.WaveformData>();
  var browser=new StubSampleBrowserService {Waveform=(_,_)=>gate.Task};
  var vm=new OHsSampleDropImportPopupVM(new[]{"a.wav"},browser,new StubSamplePreviewService(),new ImmediateUiScheduler());await vm.InitializeAsync(default);
  var read=vm.LoadWaveformAsync(vm.Entries[0]);var close=vm.DeactivateAsync();
  Assert.That(close.IsCompleted,Is.False);gate.SetResult(new(TimeSpan.FromSeconds(1),new float[]{.5f}));await close;await read;
  Assert.That(vm.Entries[0].Waveform,Is.Null);Assert.That(vm.CanConfirm,Is.False);
 }
 [Test] public async Task EqualNamesRemainIndependentlySelectable()
 {
  var vm=new OHsSampleDropImportPopupVM(new[]{"C:/one/hit.wav","C:/two/hit.wav"},new StubSampleBrowserService(),new StubSamplePreviewService(),new ImmediateUiScheduler());await vm.InitializeAsync(default);
  vm.Entries[0].IsChecked=false;Assert.That(vm.GetCheckedPaths(),Is.EqualTo(new[]{"C:/two/hit.wav"}));await vm.DeactivateAsync();
 }
}
