using Hitbench.Services.FileSystem;

namespace Hitbench.Test.Services;

public class SampleFileNameAllocationTests
{
    [Test]
    public async Task LongNameImportSaveAndMigrationPreserveOriginalsAcrossFolderCollisions()
    {
        using var project = new TestProjectFolder();
        var root = Path.GetDirectoryName(project.ProjectPath)!;
        var app = Directory.CreateDirectory(Path.Combine(root, "app")).FullName;
        var sourceFolder = Directory.CreateDirectory(Path.Combine(root, "source")).FullName;
        var name = new string('x', 251) + ".wav";
        var source = Path.Combine(sourceFolder, name); File.WriteAllBytes(source, [1, 2]);
        var files = new SampleFileService(app);
        var first = await files.ImportAsync(project.ProjectPath, source, default);
        Directory.CreateDirectory(Path.Combine(root, "Samples", SampleFileNames.GetUniqueCopyName(name, 2)));
        var next = await files.ImportAsync(project.ProjectPath, source, default);
        Assert.That(next, Does.EndWith(" (3).wav"));
        Assert.That(File.ReadAllBytes(Path.Combine(root, "Samples", first)), Is.EqualTo(new byte[] { 1, 2 }));
        var saved = await files.SaveAsync(project.ProjectPath, first, default);
        var secondSaved = await files.SaveAsync(project.ProjectPath, first, default);
        Assert.That(secondSaved, Does.EndWith(" (2).wav"));
        var legacy = Directory.CreateDirectory(Path.Combine(root, "Saved Samples")).FullName;
        File.WriteAllBytes(Path.Combine(legacy, name), [9]);
        Assert.That(await files.TryMigrateSavedSamplesAsync(project.ProjectPath, default), Is.True);
        Assert.That(File.ReadAllBytes(saved), Is.EqualTo(new byte[] { 1, 2 }));
        Assert.That(File.ReadAllBytes(Path.Combine(Path.GetDirectoryName(saved)!, SampleFileNames.GetUniqueCopyName(name, 3))), Is.EqualTo(new byte[] { 9 }));
    }

    [TestCase(2)] [TestCase(9)] [TestCase(10)] [TestCase(99)] [TestCase(100)]
    public void MaximumStemCollisionKeepsExtensionAndSuffix(long suffix)
    {
        var name = SampleFileNames.GetUniqueCopyName(new string('x', 251) + ".wav", suffix);
        Assert.That(name.Length, Is.LessThanOrEqualTo(255));
        Assert.That(name, Does.EndWith($" ({suffix}).wav"));
        Assert.That(SampleFileNames.GetFileNameError(name), Is.Null);
    }

    [Test]
    public void ShorteningDoesNotSplitSurrogates()
    {
        var name = SampleFileNames.GetUniqueCopyName(new string('x', 246) + "😀xxx.wav", 2);
        Assert.That(name, Does.Not.Contain("\ud83d (2)"));
        Assert.That(name.Length, Is.LessThanOrEqualTo(255));
    }
}
