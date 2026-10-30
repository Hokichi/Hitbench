using Hitbench.Services.FileSystem;
using NAudio.Wave;
namespace Hitbench.Test.Services;
public sealed class SampleBrowserCacheTests
{
    [Test]
    public async Task CacheLimitsAndChangedStampAreEnforced()
    {
        var root = Directory.CreateTempSubdirectory("hitbench-cache-");
        try
        {
            var master = Path.Combine(root.FullName, "master.wav");
            using (var writer = new WaveFileWriter(master, new WaveFormat(44100,16,1))) writer.Write(new byte[8820],0,8820);
            using var browser = new SampleBrowserService(openDecoder: path => new WaveFileReader(path));
            var first = await browser.ReadMetadataAsync(master, default);
            File.SetLastWriteTimeUtc(master, File.GetLastWriteTimeUtc(master).AddSeconds(10));
            Assert.That(await browser.ReadMetadataAsync(master, default), Is.Not.SameAs(first));
            for (var i = 0; i < 513; i++)
            {
                var path = Path.Combine(root.FullName, i + ".wav"); File.Copy(master,path);
                await browser.ReadMetadataAsync(path, default);
                if (i < 33) await browser.ReadWaveformAsync(path, default);
            }
            Assert.That(browser.MetadataCacheCount, Is.EqualTo(512));
            Assert.That(browser.WaveformCacheCount, Is.EqualTo(32));
            Assert.That(browser.CachedWaveformBytes, Is.LessThanOrEqualTo(8 * 1024 * 1024));
            browser.Dispose();
            Assert.That(browser.MetadataCacheCount, Is.Zero);
            Assert.That(browser.WaveformCacheCount, Is.Zero);
        }
        finally { root.Delete(true); }
    }
    [Test]
    public async Task CancellingOneMetadataWaiterPreservesTheOther()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".wav");
        using var release = new ManualResetEventSlim();
        var opened = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var opens = 0;
        try
        {
            using (var writer = new WaveFileWriter(path, new WaveFormat(44100,16,1))) writer.Write(new byte[8820],0,8820);
            using var browser = new SampleBrowserService(openDecoder: p => { Interlocked.Increment(ref opens); opened.TrySetResult(); if (!release.Wait(TimeSpan.FromSeconds(5))) throw new TimeoutException(); return new WaveFileReader(p); });
            using var cancellation = new CancellationTokenSource();
            var first = browser.ReadMetadataAsync(path, cancellation.Token);
            await opened.Task;
            var second = browser.ReadMetadataAsync(path, default);
            await Hitbench.Test.Presentation.StubWorkstationAudioService.WaitUntilAsync(() => browser.PendingMetadataWaiterCount == 2);
            cancellation.Cancel();
            Assert.CatchAsync<OperationCanceledException>(async () => await first);
            Assert.That(second.IsCompleted, Is.False);
            release.Set();
            var result = await second;
            Assert.That(await browser.ReadMetadataAsync(path, default), Is.SameAs(result));
            Assert.That(opens, Is.EqualTo(1));
        }
        finally { release.Set(); File.Delete(path); }
    }
    [Test]
    public async Task RepeatedUnchangedMetadataAndWaveformReuseDecoder()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".wav");
        try
        {
            using (var writer = new WaveFileWriter(path, new WaveFormat(44100, 16, 1))) writer.Write(new byte[8820], 0, 8820);
            var opens = 0;
            using var browser = new SampleBrowserService(openDecoder: p => { Interlocked.Increment(ref opens); return new WaveFileReader(p); });
            var first = await browser.ReadMetadataAsync(path, default);
            Assert.That(await browser.ReadMetadataAsync(path, default), Is.SameAs(first));
            var waveform = await browser.ReadWaveformAsync(path, default);
            Assert.That(await browser.ReadWaveformAsync(path, default), Is.SameAs(waveform));
            Assert.That(opens, Is.EqualTo(2));
        }
        finally { File.Delete(path); }
    }
}
