using Hitbench.Core.Entities;
using Hitbench.Services.FileSystem;
using Hitbench.Test.Presentation;

namespace Hitbench.Test.Services;

public class OHsSkinDefaultsServiceTests
{
    [TestCase(false)]
    [TestCase(true)]
    public async Task DisabledDefaultsDoesNotReadSettings(bool force)
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var cache = Path.Combine(root, "Samples", "SkinDefault");
        Directory.CreateDirectory(cache);
        try
        {
            var file = Path.Combine(cache, "soft-hitclap.wav");
            await File.WriteAllTextAsync(file, "old");
            var service = new OHsSkinDefaultsService(new StubAppSettingsService
            {
                Load = _ => throw new InvalidOperationException("Disabled defaults must not read settings.")
            });

            var result = await service.RefreshAsync(Path.Combine(root, "project.json"), "Old", false, force, default);

            Assert.That(result.SkinName, Is.EqualTo("Old"));
            Assert.That(result.Files, Is.EqualTo(new[] { "SkinDefault/soft-hitclap.wav" }));
            Assert.That(result.WasReplaced, Is.False);
            Assert.That(await File.ReadAllTextAsync(file), Is.EqualTo("old"));
        }
        finally { Directory.Delete(root, true); }
    }

    [Test]
    public async Task MissingSkinPathsReturnCachedSnapshot(
        [Values("blank", "installation", "configuration", "skin")] string missing,
        [Values(false, true)] bool force, [Values(false, true)] bool hasCache)
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var samples = Path.Combine(root, "Samples");
        var cache = Path.Combine(samples, "SkinDefault");
        var file = Path.Combine(cache, "soft-hitclap.wav");
        try
        {
            if (hasCache)
            {
                Directory.CreateDirectory(cache);
                await File.WriteAllTextAsync(file, "old");
            }
            var installation = missing == "blank" ? "" : Path.Combine(root, "osu");
            if (missing is "configuration" or "skin") Directory.CreateDirectory(installation);
            if (missing == "skin")
                await File.WriteAllTextAsync(Path.Combine(installation, "osu!.tester.cfg"), "Skin = Missing");
            var service = new OHsSkinDefaultsService(new StubAppSettingsService
            {
                Load = _ => Task.FromResult(new AppSettings { OsuPath = installation })
            }, "tester");

            var result = await service.RefreshAsync(Path.Combine(root, "project.json"), "Old", true, force, default);

            Assert.That(result.SkinName, Is.EqualTo("Old"));
            Assert.That(result.Files, Is.EqualTo(hasCache ? new[] { "SkinDefault/soft-hitclap.wav" } : Array.Empty<string>()));
            Assert.That(result.WasReplaced, Is.False);
            if (hasCache)
            {
                Assert.That(await File.ReadAllTextAsync(file), Is.EqualTo("old"));
                Assert.That(Directory.EnumerateDirectories(samples).Select(Path.GetFileName), Is.EqualTo(new[] { "SkinDefault" }));
            }
            else Assert.That(Directory.Exists(samples), Is.False);
        }
        finally { Directory.Delete(root, true); }
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task CancelledRefreshRemainsCancelled(bool allowDefaults)
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "Samples", "SkinDefault"));
        try
        {
            var file = Path.Combine(root, "Samples", "SkinDefault", "soft-hitclap.wav");
            await File.WriteAllTextAsync(file, "old");
            var service = new OHsSkinDefaultsService(new StubAppSettingsService
            {
                Load = _ => Task.FromResult(new AppSettings { OsuPath = root })
            });
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();

            Assert.CatchAsync<OperationCanceledException>(async () =>
                await service.RefreshAsync(Path.Combine(root, "project.json"), "Old", allowDefaults, false, cancellation.Token));
            Assert.That(await File.ReadAllTextAsync(file), Is.EqualTo("old"));
        }
        finally { Directory.Delete(root, true); }
    }

    [TestCase("Volume = 100")]
    [TestCase("Skin = ..")]
    [TestCase("Skin = ")]
    public async Task InvalidSkinConfigurationStillFails(string configuration)
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            await File.WriteAllTextAsync(Path.Combine(root, "osu!.tester.cfg"), configuration);
            var service = new OHsSkinDefaultsService(new StubAppSettingsService
            {
                Load = _ => Task.FromResult(new AppSettings { OsuPath = root })
            }, "tester");

            Assert.ThrowsAsync<InvalidDataException>(async () =>
                await service.RefreshAsync(Path.Combine(root, "project.json"), "Old", true, false, default));
            Assert.That(Directory.Exists(Path.Combine(root, "Samples")), Is.False);
        }
        finally { Directory.Delete(root, true); }
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task FailedOrCancelledExchangeRestoresCache(bool cancel)
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "Samples", "SkinDefault"));
        Directory.CreateDirectory(Path.Combine(root, "Skins", "New"));
        try
        {
            await File.WriteAllTextAsync(Path.Combine(root, "Samples", "SkinDefault", "soft-hitclap.wav"), "old");
            await File.WriteAllTextAsync(Path.Combine(root, "Skins", "New", "drum-hitclap.wav"), "new");
            await File.WriteAllTextAsync(Path.Combine(root, "osu!.tester.cfg"), "Skin = New");
            using var cancellation = new CancellationTokenSource();
            var service = new OHsSkinDefaultsService(new StubAppSettingsService { Load = _ => Task.FromResult(new AppSettings { OsuPath = root }) }, "tester",
                copyBytes: async (input, output, token) => { await input.CopyToAsync(output, token); if (cancel) cancellation.Cancel(); },
                moveDirectory: (source, target) => { if (source.Contains(".skin-stage-")) throw new IOException("exchange failed"); Directory.Move(source, target); });
            Assert.CatchAsync(async () => await service.RefreshAsync(Path.Combine(root, "project.json"), "Old", true, true, cancellation.Token));
            Assert.That(await File.ReadAllTextAsync(Path.Combine(root, "Samples", "SkinDefault", "soft-hitclap.wav")), Is.EqualTo("old"));
            Assert.That(Directory.EnumerateDirectories(Path.Combine(root, "Samples")).Count(), Is.EqualTo(1));
        }
        finally { Directory.Delete(root, true); }
    }
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
    public async Task MissingSkinDirectoryRetainsOldCache()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "Samples", "SkinDefault"));
        try
        {
            var path = Path.Combine(root, "Samples", "SkinDefault", "soft-hitclap.wav");
            await File.WriteAllTextAsync(path, "old");
            await File.WriteAllTextAsync(Path.Combine(root, "osu!.tester.cfg"), "Skin = Missing");
            var service = new OHsSkinDefaultsService(new StubAppSettingsService { Load = _ => Task.FromResult(new AppSettings { OsuPath = root }) }, "tester");
            var result = await service.RefreshAsync(Path.Combine(root, "project.json"), "Old", true, false, default);
            Assert.That(result.SkinName, Is.EqualTo("Old"));
            Assert.That(result.Files, Is.EqualTo(new[] { "SkinDefault/soft-hitclap.wav" }));
            Assert.That(result.WasReplaced, Is.False);
            Assert.That(await File.ReadAllTextAsync(path), Is.EqualTo("old"));
        }
        finally { Directory.Delete(root, true); }
    }
}
