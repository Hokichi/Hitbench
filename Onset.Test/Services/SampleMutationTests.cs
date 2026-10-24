using Onset.Services.FileSystem;

namespace Onset.Test.Services;

public sealed class SampleMutationTests
{
    private string root = null!;
    private string project = null!;
    private string folder = null!;
    [SetUp] public void Setup()
    {
        root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        folder = Directory.CreateDirectory(Path.Combine(root, "Samples")).FullName;
        project = Path.Combine(root, "project.json");
        File.WriteAllBytes(Path.Combine(folder, "hit.wav"), [1, 2, 3]);
    }
    [TearDown] public void Cleanup() => Directory.Delete(root, true);
    [TestCase("kick", "kick.wav")]
    [TestCase("HIT", "HIT.wav")]
    [TestCase("kick.soft", "kick.soft.wav")]
    public async Task RenamePreservesExtensionAndBytes(string stem, string expected)
    {
        var result = await new SampleFileService().RenameAsync(project, "hit.wav", stem, default);
        Assert.That(result, Is.EqualTo(expected));
        Assert.That(Directory.GetFiles(folder).Select(Path.GetFileName), Is.EqualTo(new[] { expected }));
        Assert.That(File.ReadAllBytes(Path.Combine(folder, expected)), Is.EqualTo(new byte[] { 1, 2, 3 }));
    }
    [TestCase("")][TestCase(" ")][TestCase("../hit")][TestCase("a/b")][TestCase("a\\b")]
    [TestCase("a:")][TestCase("CON")][TestCase("nul.part")][TestCase("COM1")][TestCase("LPT9")]
    [TestCase("hit ")][TestCase("hit.")]
    public void InvalidRenamePreservesOriginal(string stem)
    {
        Assert.ThrowsAsync<ArgumentException>(() => new SampleFileService().RenameAsync(project, "hit.wav", stem, default));
        Assert.That(File.ReadAllBytes(Path.Combine(folder, "hit.wav")), Is.EqualTo(new byte[] { 1, 2, 3 }));
    }
    [TestCase(false)][TestCase(true)]
    public void CollisionCannotOverwrite(bool directory)
    {
        var target = Path.Combine(folder, "KICK.wav");
        if (directory) Directory.CreateDirectory(target); else File.WriteAllBytes(target, [9]);
        Assert.ThrowsAsync<IOException>(() => new SampleFileService().RenameAsync(project, "hit.wav", "kick", default));
        Assert.That(File.Exists(Path.Combine(folder, "hit.wav")), Is.True);
        if (!directory) Assert.That(File.ReadAllBytes(target), Is.EqualTo(new byte[] { 9 }));
    }
    [Test] public async Task DeleteOnlyTouchesProjectCopyAndIsIdempotent()
    {
        var source = Path.Combine(root, "hit.wav"); File.WriteAllBytes(source, [9]);
        var service = new SampleFileService();
        await service.DeleteAsync(project, "hit.wav", default);
        await service.DeleteAsync(project, "hit.wav", default);
        Assert.That(File.Exists(Path.Combine(folder, "hit.wav")), Is.False);
        Assert.That(File.ReadAllBytes(source), Is.EqualTo(new byte[] { 9 }));
    }
    [Test] public void CancellationAndTraversalDoNotMutate()
    {
        var service = new SampleFileService();
        Assert.CatchAsync<OperationCanceledException>(() => service.RenameAsync(project, "hit.wav", "kick", new(true)));
        Assert.ThrowsAsync<ArgumentException>(() => service.DeleteAsync(project, "../hit.wav", default));
        Assert.ThrowsAsync<ArgumentException>(() => service.RenameAsync(project, "hit.wav", new string('x', 256), default));
        Assert.That(File.Exists(Path.Combine(folder, "hit.wav")), Is.True);
    }
}
