using NAudio.Wave;
using osuHitsounder.Services.FileSystem;

namespace osuHitsounder.Test.Services;

public sealed class SampleBrowserServiceTests
{
    [Test]
    public async Task ReadsRealWaveformAndReleasesDecoder()
    {
        var root = Directory.CreateTempSubdirectory();
        var path = Path.Combine(root.FullName, "hit.wav");
        try
        {
            using (var writer = new WaveFileWriter(path, new WaveFormat(44100, 16, 1)))
            {
                var bytes = Enumerable.Range(0, 8820).Select(i => (byte)(i % 128)).ToArray();
                writer.Write(bytes, 0, bytes.Length);
            }
            var data = await new SampleBrowserService().ReadWaveformAsync(path, default);
            Assert.That(data.Duration.TotalSeconds, Is.EqualTo(.1).Within(.0001));
            Assert.That(data.Peaks.Any(x => x > 0), Is.True);
            using var exclusive = File.Open(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        }
        finally { root.Delete(true); }
    }
    [Test]
    public async Task ListsDirectoriesFirstAndOnlySupportedAudioCaseInsensitively()
    {
        var root = Directory.CreateTempSubdirectory();
        try
        {
            Directory.CreateDirectory(Path.Combine(root.FullName, "child"));
            foreach (var name in new[] { "hit.WAV", "tone.ogg", "voice.mp3", "notes.txt" })
                await File.WriteAllBytesAsync(Path.Combine(root.FullName, name), [0]);
            var entries = await new SampleBrowserService().GetEntriesAsync(root.FullName, CancellationToken.None);
            Assert.That(entries.Select(entry => entry.Name), Is.EqualTo(new[] { "child", "hit.WAV", "tone.ogg", "voice.mp3" }));
            Assert.That(entries[0].IsDirectory, Is.True);
        }
        finally { root.Delete(true); }
    }

    [Test]
    public async Task DoesNotRecursivelyEnumerateChildren()
    {
        var root = Directory.CreateTempSubdirectory();
        try
        {
            var child = Directory.CreateDirectory(Path.Combine(root.FullName, "child"));
            await File.WriteAllBytesAsync(Path.Combine(child.FullName, "nested.wav"), [0]);
            var entries = await new SampleBrowserService().GetEntriesAsync(root.FullName, CancellationToken.None);
            Assert.That(entries, Has.Count.EqualTo(1));
            Assert.That(entries.Single().IsDirectory, Is.True);
        }
        finally { root.Delete(true); }
    }

    [Test]
    public async Task UnsupportedOnlyLeafReturnsNoBrowserEntries()
    {
        var root = Directory.CreateTempSubdirectory();
        try
        {
            await File.WriteAllTextAsync(Path.Combine(root.FullName, "notes.txt"), "Not audio");
            var entries = await new SampleBrowserService().GetEntriesAsync(root.FullName, default);
            Assert.That(entries, Is.Empty);
        }
        finally { root.Delete(true); }
    }

    [Test]
    public void RejectsMissingDirectoryAndHonoursCancellation()
    {
        var service = new SampleBrowserService();
        Assert.ThrowsAsync<DirectoryNotFoundException>(() => service.GetEntriesAsync(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString()), CancellationToken.None));
        Assert.CatchAsync<OperationCanceledException>(() => service.GetRootsAsync(new CancellationToken(true)));
    }

    [TestCase("wav")]
    [TestCase("ogg")]
    [TestCase("mp3")]
    public async Task ReadsMetadataWithoutRetainingFileHandle(string extension)
    {
        var root = Directory.CreateTempSubdirectory();
        var path = Path.Combine(root.FullName, "tone." + extension);
        try
        {
            if (extension == "wav")
            {
                using var writer = new WaveFileWriter(path, new WaveFormat(44_100, 16, 1));
                writer.Write(new byte[8820], 0, 8820);
            }
            else File.Copy(Path.Combine(TestContext.CurrentContext.TestDirectory, "Fixtures", "Audio", "tone." + extension), path);
            var metadata = await new SampleBrowserService().ReadMetadataAsync(path, CancellationToken.None);
            Assert.Multiple(() =>
            {
                Assert.That(metadata.FullPath, Is.EqualTo(path));
                Assert.That(metadata.Format, Is.EqualTo(extension.ToUpperInvariant()));
                Assert.That(metadata.Length, Is.EqualTo(new FileInfo(path).Length));
                Assert.That(metadata.Duration, Is.GreaterThan(TimeSpan.Zero));
                Assert.That(metadata.SampleRate, Is.GreaterThan(0));
                Assert.That(metadata.Channels, Is.GreaterThan(0));
            });
            using var exclusive = File.Open(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        }
        finally { root.Delete(true); }
    }
}
