using Hitbench.Core.Entities;
using Hitbench.Services.FileSystem;
using Hitbench.Test.Presentation;

namespace Hitbench.Test.Services;

public class OHsSkinDefaultsServiceTests
{
    [Test]
    public async Task ChangedSkinReplacesCacheAndDisablingPreservesIt()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var skin = Path.Combine(root, "Skins", "First"); Directory.CreateDirectory(skin);
            await File.WriteAllTextAsync(Path.Combine(root, "osu!.tester.cfg"), "Skin = First");
            await File.WriteAllTextAsync(Path.Combine(skin, "drum-hitclap.wav"), "first");
            await File.WriteAllTextAsync(Path.Combine(skin, "menuclick.wav"), "ignore");
            var service = new OHsSkinDefaultsService(new StubAppSettingsService { Load = _ => Task.FromResult(new AppSettings { OsuPath = root }) }, "tester");
            var project = Path.Combine(root, "project.json");
            var disabled = await service.RefreshAsync(project, "", false, false, default);
            Assert.That(disabled.Files, Is.Empty);
            var first = await service.RefreshAsync(project, "", true, false, default);
            Assert.That(first.Files, Is.EqualTo(new[] { "SkinDefault/drum-hitclap.wav" }));
            Assert.That(first.SkinName, Is.EqualTo("First"));
            Assert.That((await service.RefreshAsync(project, "First", true, false, default)).WasReplaced, Is.False);
            var secondSkin = Path.Combine(root, "Skins", "Second"); Directory.CreateDirectory(secondSkin);
            await File.WriteAllTextAsync(Path.Combine(secondSkin, "normal-hitnormal.ogg"), "second");
            await File.WriteAllTextAsync(Path.Combine(root, "osu!.tester.cfg"), "Skin = Second");
            Assert.That((await service.RefreshAsync(project, "First", false, false, default)).SkinName, Is.EqualTo("First"));
            var second = await service.RefreshAsync(project, "First", true, false, default);
            Assert.That(second.Files, Is.EqualTo(new[] { "SkinDefault/normal-hitnormal.ogg" }));
            Assert.That(File.Exists(Path.Combine(root, "Samples", "SkinDefault", "drum-hitclap.wav")), Is.False);
            Assert.That(await new SampleFileService().ListAsync(project, default), Is.Empty);
        }
        finally { Directory.Delete(root, true); }
    }

    [Test]
    public async Task FailedReplacementRetainsOldCache()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "Samples", "SkinDefault"));
        try
        {
            var path = Path.Combine(root, "Samples", "SkinDefault", "soft-hitclap.wav");
            await File.WriteAllTextAsync(path, "old");
            await File.WriteAllTextAsync(Path.Combine(root, "osu!.tester.cfg"), "Skin = Missing");
            var service = new OHsSkinDefaultsService(new StubAppSettingsService { Load = _ => Task.FromResult(new AppSettings { OsuPath = root }) }, "tester");
            Assert.ThrowsAsync<DirectoryNotFoundException>(async () => await service.RefreshAsync(Path.Combine(root, "project.json"), "Old", true, false, default));
            Assert.That(await File.ReadAllTextAsync(path), Is.EqualTo("old"));
        }
        finally { Directory.Delete(root, true); }
    }
}
