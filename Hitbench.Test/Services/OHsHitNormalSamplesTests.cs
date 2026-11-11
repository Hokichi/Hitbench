using Hitbench.Core.Entities;
using Hitbench.Resources.DataModels;
using Hitbench.Services.FileSystem;
using Hitbench.Services.ProjectFiles;

namespace Hitbench.Test.Services;

public class OHsHitNormalSamplesTests
{
    [Test]
    public void LegacyProjectDefaultsAllHitNormalPaths()
    {
        var config = OHsHitNormalConfiguration.FromProject(ProjectDataSerializer.Deserialize("{}"));
        Assert.That(config, Is.EqualTo(new OHsHitNormalConfiguration()));
        var project = new ProjectData();
        new OHsHitNormalConfiguration("a.wav", "Mapset/b.ogg", "c.mp3").ApplyTo(project);
        Assert.That(OHsHitNormalConfiguration.FromProject(ProjectDataSerializer.Deserialize(ProjectDataSerializer.Serialize(project))),
            Is.EqualTo(new OHsHitNormalConfiguration("a.wav", "Mapset/b.ogg", "c.mp3")));
    }

    [TestCase("null")][TestCase("12")][TestCase("\"../bad.wav\"")][TestCase("\"bad.txt\"")]
    public void MalformedHitNormalMembersAreRejected(string value) =>
        Assert.Throws<InvalidDataException>(() => ProjectDataSerializer.Deserialize("{\"NormalHitNormalPath\":" + value + "}"));

    [Test]
    public void DuplicateHitNormalMembersAreRejected() => Assert.Throws<InvalidDataException>(() =>
        ProjectDataSerializer.Deserialize("{\"NormalHitNormalPath\":\"\",\"normalhitnormalpath\":\"\"}"));

    [Test]
    public async Task ChoicesKeepSkinProjectAndMapsetSourcesDistinct()
    {
        var root = Directory.CreateTempSubdirectory();
        try
        {
            var project = Path.Combine(root.FullName, "project", "project.json");
            var samples = Directory.CreateDirectory(Path.Combine(root.FullName, "project", "Samples"));
            var mapset = root.CreateSubdirectory("mapset");
            await File.WriteAllTextAsync(Path.Combine(samples.FullName, "normal-hitnormal.wav"), "project");
            await File.WriteAllTextAsync(Path.Combine(mapset.FullName, "normal-hitnormal.wav"), "mapset");
            var skin = samples.CreateSubdirectory("SkinDefault");
            await File.WriteAllTextAsync(Path.Combine(skin.FullName, "normal-hitnormal.wav"), "skin");
            var service = new OHsHitNormalSamples(new SampleFileService(root.FullName));
            var choices = await service.ListAsync(project, mapset.FullName, default);
            Assert.That(choices[0], Is.EqualTo(new OHsHitNormalChoice("", "Default")));
            Assert.That(choices.Count, Is.EqualTo(4));
            Assert.That(choices.Select(c => c.DisplayName).Distinct().Count(), Is.EqualTo(4));
            Assert.That(choices.Select(c => c.Path), Does.Contain("SkinDefault/normal-hitnormal.wav"));
            Assert.That(choices.Single(c => c.Path.StartsWith("SkinDefault/")).Source, Is.EqualTo(OHsHitNormalSampleSource.Skin));
            Assert.That(choices.Single(c => c.Path == "normal-hitnormal.wav").Source, Is.EqualTo(OHsHitNormalSampleSource.Project));
            Assert.That(choices.Single(c => Path.IsPathFullyQualified(c.Path)).Source, Is.EqualTo(OHsHitNormalSampleSource.Mapset));
            Assert.That(choices.Select(c => c.Path), Does.Contain("normal-hitnormal.wav"));
            Assert.That(choices.Select(c => c.Path), Does.Contain(Path.Combine(mapset.FullName, "normal-hitnormal.wav")));
            Assert.Throws<ArgumentException>(() => OHsHitNormalSamples.Resolve(project, mapset.FullName, Path.Combine(root.FullName, "outside.wav")));
            Assert.Throws<ArgumentException>(() => OHsHitNormalSamples.Resolve(project, mapset.FullName, "../escape.wav"));
            using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
            Assert.ThrowsAsync<OperationCanceledException>(async () => await service.ListAsync(project, mapset.FullName, cancellation.Token));
            Assert.That(await service.ListAsync(project, Path.Combine(root.FullName, "missing"), default), Has.Count.EqualTo(3));
        }
        finally { root.Delete(true); }
    }

    [Test]
    public void ExplicitSkinChoiceRoundTripsAndResolvesInsideSamples()
    {
        var project = new ProjectData { NormalHitNormalPath = "SkinDefault/soft-hitclap.wav" };
        var copy = ProjectDataSerializer.Deserialize(ProjectDataSerializer.Serialize(project));
        Assert.That(copy.NormalHitNormalPath, Is.EqualTo(project.NormalHitNormalPath));
        var path = Path.Combine(Path.GetTempPath(), "hitnormal-project", "project.json");
        Assert.That(OHsHitNormalSamples.Resolve(path, "", copy.NormalHitNormalPath),
            Is.EqualTo(Path.Combine(Path.GetDirectoryName(path)!, "Samples", "SkinDefault", "soft-hitclap.wav")));
    }

    [TestCase("SkinDefault/../escape.wav")]
    [TestCase("SkinDefault/nested/normal-hitnormal.wav")]
    [TestCase("SkinDefault/random.wav")]
    [TestCase("SkinDefault/normal-hitnormal.txt")]
    public void InvalidSkinPathsAreRejected(string path) =>
        Assert.That(OHsHitNormalSamples.GetPathError(path), Is.Not.Null);

    [TestCase(false)] [TestCase(true)]
    public void LinkedSkinFoldersAndFilesCannotResolve(bool fileLink)
    {
        var root = Directory.CreateTempSubdirectory();
        var outside = Directory.CreateTempSubdirectory();
        string? link = null;
        try
        {
            var samples = root.CreateSubdirectory("Samples");
            var target = Path.Combine(outside.FullName, "normal-hitnormal.wav");
            File.WriteAllText(target, "outside");
            if (fileLink)
            {
                var skin = samples.CreateSubdirectory("SkinDefault");
                link = Path.Combine(skin.FullName, "normal-hitnormal.wav");
                File.CreateSymbolicLink(link, target);
            }
            else
            {
                link = Path.Combine(samples.FullName, "SkinDefault");
                Directory.CreateSymbolicLink(link, outside.FullName);
            }
            Assert.Throws<ArgumentException>(() => OHsHitNormalSamples.Resolve(Path.Combine(root.FullName, "project.json"), "", "SkinDefault/normal-hitnormal.wav"));
        }
        finally
        {
            if (link is not null) { if (fileLink) File.Delete(link); else Directory.Delete(link); }
            root.Delete(true); outside.Delete(true);
        }
    }
}
