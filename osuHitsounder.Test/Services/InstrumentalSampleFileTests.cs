using osuHitsounder.Services.FileSystem;

namespace osuHitsounder.Test.Services;

public sealed class InstrumentalSampleFileTests
{
    private string root = null!, project = null!;
    [SetUp] public void Setup()
    {
        root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"))).FullName;
        project = Path.Combine(root, "project.json");
    }
    [TearDown] public void Cleanup() => Directory.Delete(root, true);

    [Test] public async Task NestedCopiesCanBeListedRenamedAndDeletedWithoutOverwriting()
    {
        var service = new SampleFileService();
        var folder = await service.EnsureFolderAsync(project, "R&B", "", [], default);
        Assert.That(folder, Is.EqualTo("RB"));
        var source = Path.Combine(root, "C3.wav"); File.WriteAllBytes(source, [1, 2]);
        Assert.That(await service.ImportAsync(project, source, default, folder), Is.EqualTo("RB/C3.wav"));
        Assert.That(await service.ImportAsync(project, source, default, folder), Is.EqualTo("RB/C3 (2).wav"));
        Assert.That(await service.ImportAsync(project, Path.Combine(root, "Samples", "RB", "C3.wav"), default, folder), Is.EqualTo("RB/C3.wav"));
        Assert.That(await service.RenameAsync(project, "RB/C3.wav", "D3", default), Is.EqualTo("RB/D3.wav"));
        Assert.That(await service.ListAsync(project, default), Is.EqualTo(new[] { "RB/C3 (2).wav", "RB/D3.wav" }));
        await service.DeleteAsync(project, "RB/D3.wav", default);
        Assert.That(File.ReadAllBytes(source), Is.EqualTo(new byte[] { 1, 2 }));
        Assert.That(File.ReadAllBytes(Path.Combine(root, "Samples", "RB", "C3 (2).wav")), Is.EqualTo(new byte[] { 1, 2 }));
    }

    [Test] public async Task AllocatesAroundDiskAndPersistedReservationsAndRenamesCaseOnly()
    {
        var service = new SampleFileService();
        Directory.CreateDirectory(Path.Combine(root, "Samples", "RB"));
        var folder = await service.EnsureFolderAsync(project, "R&B", "", ["RB (2)"], default);
        Assert.That(folder, Is.EqualTo("RB (3)"));
        File.WriteAllBytes(Path.Combine(root, "Samples", folder, "C3.wav"), [4]);
        var renamed = await service.RenameFolderAsync(project, folder, "Piano", ["RB (2)"], default);
        Assert.That(renamed, Is.EqualTo("Piano"));
        Assert.That(await service.RenameFolderAsync(project, renamed, "piano", [], default), Is.EqualTo("piano"));
        Assert.That(File.ReadAllBytes(Path.Combine(root, "Samples", "piano", "C3.wav")), Is.EqualTo(new byte[] { 4 }));
    }

    [Test] public async Task DiscoveryKeepsAllContendersAndIgnoresNonNotesAndSubfolders()
    {
        File.WriteAllBytes(Path.Combine(root, "C3.wav"), [1]);
        File.WriteAllBytes(Path.Combine(root, "C3.mp3"), [2]);
        File.WriteAllBytes(Path.Combine(root, "c#4.OGG"), [3]);
        File.WriteAllBytes(Path.Combine(root, "C0.wav"), [4]);
        Directory.CreateDirectory(Path.Combine(root, "Nested"));
        File.WriteAllBytes(Path.Combine(root, "Nested", "D4.wav"), [4]);
        var result = await new SampleFileService().FindNoteFilesAsync(root, default);
        Assert.That(result.Candidates.Keys, Is.EquivalentTo(new[] { "C3", "C#4" }));
        Assert.That(result.Candidates["C3"].Select(Path.GetFileName), Is.EqualTo(new[] { "C3.mp3", "C3.wav" }));
        Assert.That(result.IgnoredPaths.Select(Path.GetFileName), Is.EquivalentTo(new[] { "C0.wav", "Nested" }));
    }

    [Test] public async Task FailedCopyAndEmptyFolderCleanupNeverDeleteExistingFiles()
    {
        var service = new SampleFileService();
        var folder = await service.EnsureFolderAsync(project, "Piano", "", [], default);
        Assert.ThrowsAsync<FileNotFoundException>(() => service.ImportAsync(project, Path.Combine(root, "missing.wav"), default, folder));
        Assert.That(Directory.GetFiles(Path.Combine(root, "Samples", folder)), Is.Empty);
        await service.DeleteEmptyFolderAsync(project, folder, default);
        Assert.That(Directory.Exists(Path.Combine(root, "Samples", folder)), Is.False);
        Directory.CreateDirectory(Path.Combine(root, "Samples", folder));
        File.WriteAllBytes(Path.Combine(root, "Samples", folder, "C3.wav"), [1]);
        await service.DeleteEmptyFolderAsync(project, folder, default);
        Assert.That(File.Exists(Path.Combine(root, "Samples", folder, "C3.wav")), Is.True);
    }

    [Test] public void LinkedSampleDirectoryCannotEscapeProject()
    {
        var outside = Directory.CreateDirectory(Path.Combine(root, "outside")).FullName;
        var samples = Directory.CreateDirectory(Path.Combine(root, "Samples")).FullName;
        Directory.CreateSymbolicLink(Path.Combine(samples, "Link"), outside);
        Assert.Throws<ArgumentException>(() => SamplePaths.Resolve(project, "Link/C3.wav"));
    }
}
