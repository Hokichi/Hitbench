using Hitbench.Services.FileSystem;

namespace Hitbench.Test.Services;

public class SampleFileNameAllocationTests
{
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
