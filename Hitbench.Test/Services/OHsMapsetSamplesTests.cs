using Hitbench.Services.FileSystem;

namespace Hitbench.Test.Services;

public sealed class OHsMapsetSamplesTests
{
    [TestCase("drum-hitnormal9.wav", true)]
    [TestCase("SOFT-HITCLAP12.OGG", true)]
    [TestCase("normal-hitfinish.mp3", true)]
    [TestCase("normal-hitnormal9 (2).wav", false)]
    [TestCase("normal-hitnormal9.wav\n", false)]
    [TestCase("normal-sliderslide.wav", false)]
    [TestCase("song.mp3", false)]
    [TestCase("drum-hitnormal９.wav", false)]
    public void MatchesOnlyCompleteHitsoundNames(string name, bool expected) =>
        Assert.That(OHsMapsetSamples.IsMatchingFileName(name), Is.EqualTo(expected));

    [Test]
    public async Task DiscoveryExcludesNestedAndUnrelatedFiles()
    {
        var folder = Directory.CreateTempSubdirectory("Hitbench-mapset-");
        try
        {
            File.WriteAllText(Path.Combine(folder.FullName, "drum-hitnormal9.wav"), "sample");
            File.WriteAllText(Path.Combine(folder.FullName, "song.mp3"), "song");
            var child = folder.CreateSubdirectory("child");
            File.WriteAllText(Path.Combine(child.FullName, "soft-hitclap.wav"), "nested");
            Assert.That((await OHsMapsetSamples.FindAsync(folder.FullName, default)).Select(Path.GetFileName),
                Is.EqualTo(new[] { "drum-hitnormal9.wav" }));
        }
        finally { folder.Delete(true); }
    }

    [Test]
    public void EveryFamilyTypeAndSupportedExtensionMatches()
    {
        foreach(var family in new[]{"normal","soft","drum"})
        foreach(var type in new[]{"normal","whistle","finish","clap"})
        foreach(var extension in new[]{"wav","ogg","mp3"})
        foreach(var index in new[]{"","0","12"})
            Assert.That(OHsMapsetSamples.IsMatchingFileName($"{family}-hit{type}{index}.{extension}"),Is.True);
    }
    [TestCase("Mapset/renamed.wav",true)] [TestCase("mapset/renamed.OGG",true)]
    [TestCase("Mapset/sub/normal-hitnormal.wav",false)] [TestCase("Piano/C3.wav",false)]
    [TestCase("../Mapset/normal-hitnormal.wav",false)] [TestCase("Mapset/../escape.wav",false)]
    public void GroupIdentityUsesDirectFolderMembership(string path,bool expected) =>
        Assert.That(OHsMapsetSamples.IsMapsetSamplePath(path),Is.EqualTo(expected));
    [Test]
    public void CancelledDiscoveryDoesNotReturnResults()
    {
        using var cancellation=new CancellationTokenSource();cancellation.Cancel();
        Assert.ThrowsAsync<TaskCanceledException>(()=>OHsMapsetSamples.FindAsync(Path.GetTempPath(),cancellation.Token));
    }
}
