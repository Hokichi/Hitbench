using Soundie.Services.FileSystem;

namespace Soundie.Test.Services;

public sealed class SamplePathsTests
{
    [Test]
    public void SavedSamplesInitializationPreservesBothFoldersAndHandlesFailure()
    {
        var root = Directory.CreateTempSubdirectory();
        try
        {
            var project = Path.Combine(root.FullName, "project.json");
            var saved = Path.Combine(root.FullName, "Saved Samples");
            Assert.That(SamplePaths.SavedRoot(project), Is.EqualTo(saved));
            Assert.That(SamplePaths.TryEnsureSavedRoot(project, default), Is.True);
            File.WriteAllText(Path.Combine(saved, "keep.wav"), "saved");
            Directory.CreateDirectory(Path.Combine(root.FullName, "Samples"));
            File.WriteAllText(Path.Combine(root.FullName, "Samples", "keep.wav"), "imported");
            Assert.That(SamplePaths.TryEnsureSavedRoot(project, default), Is.True);
            Assert.That(File.ReadAllText(Path.Combine(saved, "keep.wav")), Is.EqualTo("saved"));
            Assert.That(File.ReadAllText(Path.Combine(root.FullName, "Samples", "keep.wav")), Is.EqualTo("imported"));
            Assert.That(SamplePaths.TryEnsureSavedRoot(project, default, _ => throw new UnauthorizedAccessException()), Is.False);
            Assert.Throws<OperationCanceledException>(() => SamplePaths.TryEnsureSavedRoot(project, new CancellationToken(true)));
        }
        finally { root.Delete(true); }
    }

    [Test]
    public void SavedSamplesNeverOverwritesAFileOrFollowsALink()
    {
        var root = Directory.CreateTempSubdirectory();
        var outside = Directory.CreateTempSubdirectory();
        try
        {
            var project = Path.Combine(root.FullName, "project.json");
            var saved = Path.Combine(root.FullName, "Saved Samples");
            File.WriteAllText(saved, "keep");
            Assert.That(SamplePaths.TryEnsureSavedRoot(project, default), Is.False);
            Assert.That(File.ReadAllText(saved), Is.EqualTo("keep"));
            File.Delete(saved);
            Directory.CreateSymbolicLink(saved, outside.FullName);
            Assert.That(SamplePaths.TryEnsureSavedRoot(project, default), Is.False);
            Assert.That(Directory.EnumerateFileSystemEntries(outside.FullName), Is.Empty);
            Directory.Delete(saved);
        }
        finally { root.Delete(true); outside.Delete(true); }
    }
    [TestCase("R&B", "RB")]
    [TestCase("&", "Instrumental")]
    [TestCase(" CON ", "_CON")]
    [TestCase(" Piano / Soft ", "Piano  Soft")]
    public void NormalizesFolderNames(string input, string expected) =>
        Assert.That(SamplePaths.NormalizeFolderName(input), Is.EqualTo(expected));

    [Test] public void LeavesRoomForSampleFileNames() =>
        Assert.That(SamplePaths.NormalizeFolderName(new string('A', 81)), Is.EqualTo(new string('A', 80)));

    [TestCase("../C3.wav")][TestCase("Piano/../C3.wav")][TestCase("C:/C3.wav")]
    [TestCase("/C3.wav")][TestCase("Piano//C3.wav")][TestCase("CON/C3.wav")][TestCase("Piano/C3.flac")]
    public void RejectsUnsafeRelativeReferences(string path) =>
        Assert.That(SamplePaths.GetRelativePathError(path), Is.Not.Null);

    [TestCase("C3.wav")][TestCase("Piano/C#3.OGG")]
    public void AcceptsRootAndNestedAudio(string path) =>
        Assert.That(SamplePaths.GetRelativePathError(path), Is.Null);
}
