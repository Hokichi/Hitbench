using CommunityToolkit.Mvvm.Messaging;
using Hitbench.Resources.DataModels;
using Hitbench.Services.FileSystem;
using Hitbench.ViewModels.Shell.HomePage;

namespace Hitbench.Test.Presentation;

public class FirstRunSkinLoadingTests
{
    [Test]
    public async Task DetectedInstallationEnablesSkinImportAndPersistsBothPaths()
    {
        var root = Directory.CreateTempSubdirectory("hitbench-first-run-");
        HomePageVM? vm = null;
        try
        {
            var installation = Path.Combine(root.FullName, "osu");
            var songs = Path.Combine(installation, "Songs");
            Directory.CreateDirectory(songs);
            var skin = Directory.CreateDirectory(Path.Combine(installation, "Skins", "TestSkin"));
            byte[] bytes = [1, 2, 3, 4];
            await File.WriteAllBytesAsync(Path.Combine(skin.FullName, "normal-hitnormal.wav"), bytes);
            await File.WriteAllTextAsync(Path.Combine(installation, "osu!.tester.cfg"), "Skin = TestSkin");
            using var settings = new AppSettingsService(root.FullName);
            vm = new HomePageVM(new StubHomeFolderService
            {
                Find = _ => Task.FromResult(new OsuSearchResult(installation, false))
            }, settings, new StubInstallationDialogService(), new ImmediateUiScheduler(),
                new StubProjectFileService((_, _, _) => throw new AssertionException("Unexpected project creation")),
                new WeakReferenceMessenger());
            await vm.LoadAsync(default);
            var project = Path.Combine(root.FullName, "project.json");
            var result = await new OHsSkinDefaultsService(settings, "tester")
                .RefreshAsync(project, "", true, false, default);
            Assert.Multiple(() =>
            {
                Assert.That(result.SkinName, Is.EqualTo("TestSkin"));
                Assert.That(result.WasReplaced, Is.True);
                Assert.That(result.Files, Is.EqualTo(new[] { "SkinDefault/normal-hitnormal.wav" }));
            });
            Assert.That(await File.ReadAllBytesAsync(Path.Combine(root.FullName, "Samples", "SkinDefault", "normal-hitnormal.wav")), Is.EqualTo(bytes));
            using var reloaded = new AppSettingsService(root.FullName);
            var saved = await reloaded.LoadAsync(default);
            Assert.Multiple(() =>
            {
                Assert.That(saved.OsuPath, Is.EqualTo(installation));
                Assert.That(saved.MapsetsDirectory, Is.EqualTo(songs));
            });
        }
        finally { vm?.Deactivate(); root.Delete(true); }
    }
}
