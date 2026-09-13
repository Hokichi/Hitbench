using Newtonsoft.Json.Linq;
using osuHitsounder.Services.FileSystem;

namespace osuHitsounder.Test.Services;

public class AppSettingsServiceTests
{
    private DirectoryInfo root = null!;
    private string FilePath => Path.Combine(root.FullName, "appsettings.json");
    [SetUp] public void SetUp() => root = Directory.CreateTempSubdirectory("osu-settings-");
    [TearDown] public void TearDown() => root.Delete(true);

    [Test]
    public async Task SeedsMissingSettingsAndPreservesOtherPropertiesOnSave()
    {
        using var service = new AppSettingsService(root.FullName);
        Assert.That((await service.LoadAsync(CancellationToken.None)).OsuPath, Is.Empty);
        Assert.That(JObject.Parse(await File.ReadAllTextAsync(FilePath))["osuPath"]!.Value<string>(), Is.Empty);
        await File.WriteAllTextAsync(FilePath, "{\"keep\":17}");
        Assert.That((await service.LoadAsync(CancellationToken.None)).OsuPath, Is.Empty);
        await service.SaveOsuPathAsync(root.FullName, CancellationToken.None);
        var json = JObject.Parse(await File.ReadAllTextAsync(FilePath));
        Assert.That(json["keep"]!.Value<int>(), Is.EqualTo(17));
        Assert.That(json["osuPath"]!.Value<string>(), Is.EqualTo(root.FullName));
    }

    [TestCase("{\"osuPath\":null}")]
    [TestCase("{\"osuPath\":\"  \"}")]
    public async Task NormalizesUnsetPath(string json)
    {
        await File.WriteAllTextAsync(FilePath, json);
        using var service = new AppSettingsService(root.FullName);
        Assert.That((await service.LoadAsync(CancellationToken.None)).OsuPath, Is.Empty);
    }

    [TestCase("broken")]
    [TestCase("[]")]
    [TestCase("{\"osuPath\":12}")]
    public async Task InvalidSettingsArePreserved(string json)
    {
        await File.WriteAllTextAsync(FilePath, json);
        using var service = new AppSettingsService(root.FullName);
        Assert.ThrowsAsync<IOException>(() => service.LoadAsync(CancellationToken.None));
        Assert.That(await File.ReadAllTextAsync(FilePath), Is.EqualTo(json));
    }

    [Test]
    public async Task FailedReplacementKeepsOriginalAndRemovesTemporaryFile()
    {
        const string original = "{\"osuPath\":\"old\"}";
        await File.WriteAllTextAsync(FilePath, original);
        using var service = new AppSettingsService(root.FullName);
        using (var held = new FileStream(FilePath, FileMode.Open, FileAccess.Read, FileShare.Read))
            Assert.ThrowsAsync<IOException>(() => service.SaveOsuPathAsync(root.FullName, CancellationToken.None));
        Assert.That(await File.ReadAllTextAsync(FilePath), Is.EqualTo(original));
        Assert.That(root.GetFiles(), Has.Length.EqualTo(1));
    }

    [Test]
    public void CancelledLoadDoesNotCreateFile()
    {
        using var service = new AppSettingsService(root.FullName);
        Assert.CatchAsync<OperationCanceledException>(() => service.LoadAsync(new CancellationToken(true)));
        Assert.That(File.Exists(FilePath), Is.False);
    }
}

