using Hitbench.Resources.DataModels;

namespace Hitbench.Test.Services;

public sealed class OHsReleaseVersionTests
{
    [TestCase("1.2.3", "official")]
    [TestCase("1.2.3-beta", "beta")]
    [TestCase("1.2.3beta", "beta")]
    [TestCase("1.2.3-hotfix", "hotfix")]
    [TestCase("1.2.3-BETA.2", "beta")]
    public void ParsesNumericVersionAndDescription(string tag, string description)
    {
        Assert.That(OHsReleaseVersion.TryParse(tag, out var version), Is.True);
        Assert.That(version!.Tag, Is.EqualTo(tag));
        Assert.That(version.NumericVersion, Is.EqualTo(new Version(1, 2, 3)));
        Assert.That(version.Description, Is.EqualTo(description));
    }

    [TestCase(null)] [TestCase("")] [TestCase("1.2")] [TestCase("1.2.3-")]
    [TestCase("1.2.3-beta..")] [TestCase("1.2.3/evil")] [TestCase("1.2.3 beta")]
    [TestCase("9999999999999.2.3")] [TestCase("1.2.3.4")] [TestCase("1.2.3-beta.lock")]
    public void RejectsMalformedVersion(string? tag) => Assert.That(OHsReleaseVersion.TryParse(tag, out _), Is.False);
}
