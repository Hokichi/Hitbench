using System.Windows.Media;
using System.Windows.Media.Imaging;
using osuHitsounder.Services.Ui;

namespace osuHitsounder.Test.Presentation;

public class SongImageLoaderTests
{
    [Test]
    public async Task EmptyArtworkPathReturnsNoImage()
    {
        Assert.That(await new SongImageLoader().LoadAsync("", 336, CancellationToken.None), Is.Null);
    }

    [Test]
    public async Task ThumbnailCacheEvictsOldImagesAtMemoryLimit()
    {
        var path = Path.GetTempFileName();
        try
        {
            var bitmap = BitmapSource.Create(2048, 2048, 96, 96, PixelFormats.Bgra32,
                null, new byte[2048 * 2048 * 4], 2048 * 4);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using (var stream = File.Create(path)) encoder.Save(stream);
            var loader = new SongImageLoader();
            var first = await loader.LoadAsync(path, 2048, CancellationToken.None);
            for (var width = 2049; width <= 2052; width++)
                await loader.LoadAsync(path, width, CancellationToken.None);
            Assert.That(await loader.LoadAsync(path, 2048, CancellationToken.None), Is.Not.SameAs(first));
        }
        finally { File.Delete(path); }
    }

    [Test]
    public async Task DecodesFrozenThumbnailAndReusesIt()
    {
        var path = Path.GetTempFileName();
        try
        {
            var bitmap = BitmapSource.Create(800, 400, 96, 96, PixelFormats.Bgra32, null, new byte[800 * 400 * 4], 800 * 4);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using (var stream = File.Create(path)) encoder.Save(stream);
            var loader = new SongImageLoader();
            var first = await loader.LoadAsync(path, 336, CancellationToken.None);
            Assert.That(first, Is.Not.Null);
            Assert.That(first!.IsFrozen, Is.True);
            Assert.That(first.PixelWidth, Is.EqualTo(336));
            Assert.That(await loader.LoadAsync(path, 336, CancellationToken.None), Is.SameAs(first));
            loader.Clear();
            Assert.That(await loader.LoadAsync(path, 336, CancellationToken.None), Is.Not.SameAs(first));
        }
        finally { File.Delete(path); }
    }

    [Test]
    public async Task MissingOrCorruptImagesDoNotFailLoading()
    {
        var loader = new SongImageLoader();
        Assert.That(await loader.LoadAsync(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString()), 336, CancellationToken.None), Is.Null);
        var path = Path.GetTempFileName();
        try { Assert.That(await loader.LoadAsync(path, 336, CancellationToken.None), Is.Null); }
        finally { File.Delete(path); }
        Assert.ThrowsAsync<OperationCanceledException>(async () =>
            await loader.LoadAsync(path, 336, new CancellationToken(true)));
    }
}
