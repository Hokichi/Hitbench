using osuHitsounder.Services.FileSystem;

namespace osuHitsounder.Test.Services;

public sealed class SamplePathsTests
{
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
