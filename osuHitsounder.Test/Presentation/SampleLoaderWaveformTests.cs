using osuHitsounder.Resources.DataModels;
using osuHitsounder.Services;
using osuHitsounder.ViewModels.Popups;

namespace osuHitsounder.Test.Presentation;

public sealed class SampleLoaderWaveformTests
{
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
