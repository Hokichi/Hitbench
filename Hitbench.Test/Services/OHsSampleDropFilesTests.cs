using Hitbench.Services.FileSystem;
namespace Hitbench.Test.Services;

public sealed class OHsSampleDropFilesTests
{
    [Test]
    public async Task FiltersActualAudioFilesAndDeduplicates()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        try
        {
            var a = Path.Combine(root, "a.WAV"); var b = Path.Combine(root, "b.ogg"); var c = Path.Combine(root, "c.mp3"); var no = Path.Combine(root, "no.txt"); var dir = Path.Combine(root, "folder.wav");
            foreach (var p in new[] { a, b, c, no }) await File.WriteAllTextAsync(p, ""); Directory.CreateDirectory(dir);
            Assert.That(await OHsSampleDropFiles.GetEligibleAsync(new[] { a, b, c, no, dir, a.ToUpperInvariant() }, default), Is.EqualTo(new[] { a, b, c }));
        }
        finally { Directory.Delete(root, true); }
    }
}
