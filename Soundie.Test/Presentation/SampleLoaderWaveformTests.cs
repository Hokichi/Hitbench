using Soundie.Resources.DataModels;
using Soundie.Services;
using Soundie.ViewModels.Popups;

namespace Soundie.Test.Presentation;

public sealed class SampleLoaderWaveformTests
{
    [Test]
    public async Task StopWhileMetadataIsPendingPreventsDelayedAutoplay()
    {
        var browser = new StubSampleBrowserService();
        var file = new SampleBrowserEntry(Path.Combine(browser.RootPath, "hit.wav"), "hit.wav", false);
        browser.DefaultDirectory = browser.RootPath; browser.Entries[browser.RootPath] = [file];
        var vm = new SampleLoaderPopupVM(browser, new StubSamplePreviewService(), new ImmediateUiScheduler(), new PlaybackVolumeSettings());
        var metadata = new TaskCompletionSource<SampleFileMetadata>();
        try
        {
            await vm.InitializeAsync(default(CancellationToken));
            browser.Metadata = (_, _) => metadata.Task;
            var selecting = vm.SelectAsync(file);
            await vm.StopPreviewCommand.ExecuteAsync(null);
            metadata.SetResult(new(file.FullPath, file.Name, "WAV", 1024, TimeSpan.FromSeconds(1), 44100, 1));
            await selecting;
            Assert.That(vm.IsPreviewPlaying, Is.False);
            Assert.That(vm.PlaybackPosition, Is.EqualTo(TimeSpan.Zero));
        }
        finally { metadata.TrySetCanceled(); await vm.DeactivateAsync(); }
    }
    [Test]
    public async Task LateWaveformCannotReplaceANewerSelection()
    {
        var browser = new StubSampleBrowserService();
        var first = new SampleBrowserEntry(Path.Combine(browser.RootPath, "first.wav"), "first.wav", false);
        var second = new SampleBrowserEntry(Path.Combine(browser.RootPath, "second.wav"), "second.wav", false);
        browser.DefaultDirectory = browser.RootPath; browser.Entries[browser.RootPath] = [first, second];
        var older = new TaskCompletionSource<Soundie.Core.Entities.WaveformData>();
        var newer = new Soundie.Core.Entities.WaveformData(TimeSpan.FromSeconds(2), new float[] { .8f });
        browser.Waveform = (path, _) => path == first.FullPath ? older.Task : Task.FromResult(newer);
        var vm = new SampleLoaderPopupVM(browser, new StubSamplePreviewService(), new ImmediateUiScheduler(), new PlaybackVolumeSettings()) { AutoPlaySamples = false };
        try
        {
            await vm.InitializeAsync(default(CancellationToken));
            await vm.SelectAsync(first); await vm.SelectAsync(second);
            Assert.That(vm.Waveform, Is.SameAs(newer));
            older.SetResult(new(TimeSpan.FromSeconds(1), new float[] { .1f }));
            await Task.Yield();
            Assert.That(vm.Waveform, Is.SameAs(newer));
            Assert.That(vm.SelectedFile, Is.SameAs(second));
        }
        finally { older.TrySetCanceled(); await vm.DeactivateAsync(); }
    }
    [Test]
    public async Task WaveformAndSeparatePlayStopUseActiveSample()
    {
        var browser = new StubSampleBrowserService();
        var file = new SampleBrowserEntry(Path.Combine(browser.RootPath, "hit.wav"), "hit.wav", false);
        browser.DefaultDirectory = browser.RootPath; browser.Entries[browser.RootPath] = [file];
        var preview = new StubSamplePreviewService();
        var vm = new SampleLoaderPopupVM(browser, preview, new ImmediateUiScheduler(), new PlaybackVolumeSettings()) { AutoPlaySamples = false };
        try
        {
            await vm.InitializeAsync(default(CancellationToken));
            await vm.SelectAsync(file);
            Assert.That(vm.Waveform, Is.Not.Null);
            await vm.SeekPreviewCommand.ExecuteAsync(TimeSpan.FromSeconds(.35));
            Assert.That(vm.PlaybackPosition, Is.EqualTo(TimeSpan.FromSeconds(.35)));
            await vm.PlayPreviewCommand.ExecuteAsync(null);
            Assert.That(vm.PlayPreviewCommand.CanExecute(null), Is.False);
            Assert.That(file.IsPreviewing, Is.True);
            await vm.StopPreviewCommand.ExecuteAsync(null);
            Assert.That(vm.PlaybackPosition, Is.EqualTo(TimeSpan.Zero));
            Assert.That(file.IsPreviewing, Is.False);
        }
        finally { await vm.DeactivateAsync(); }
    }
}
