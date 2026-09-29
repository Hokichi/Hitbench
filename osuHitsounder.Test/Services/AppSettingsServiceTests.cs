using Newtonsoft.Json.Linq;
using osuHitsounder.Services.FileSystem;

namespace osuHitsounder.Test.Services;

public class AppSettingsServiceTests
{
    [TestCase("{}", null)]
    [TestCase("{\"asioDriverName\":null}", null)]
    [TestCase("{\"asioDriverName\":\"\"}", null)]
    [TestCase("{\"asioDriverName\":\"  \"}", null)]
    [TestCase("{\"asioDriverName\":\" Studio ASIO \"}", " Studio ASIO ")]
    public async Task OptionalDriverSelectionPreservesExactNonblankName(string json, string? expected)
    {
        await File.WriteAllTextAsync(FilePath,json);
        using var service = new AppSettingsService(root.FullName);
        Assert.That((await service.LoadAsync(CancellationToken.None)).AsioDriverName,Is.EqualTo(expected));
    }

    [Test]
    public async Task SavingOtherSettingsPreservesDriverAndUnknownFields()
    {
        await File.WriteAllTextAsync(FilePath,"{\"asioDriverName\":\"Studio ASIO\",\"keep\":17}");
        using var service = new AppSettingsService(root.FullName);
        await service.SaveWindowPlacementAsync(false,1280,720,CancellationToken.None);
        await service.SaveOsuPathAsync(root.FullName,CancellationToken.None);
        Assert.That((await service.LoadAsync(CancellationToken.None)).AsioDriverName,Is.EqualTo("Studio ASIO"));
        Assert.That(JObject.Parse(await File.ReadAllTextAsync(FilePath))["keep"]!.Value<int>(),Is.EqualTo(17));
    }

    [TestCase("12")]
    [TestCase("[]")]
    [TestCase("true")]
    public async Task DriverSettingRejectsNonStringValues(string value)
    {
        await File.WriteAllTextAsync(FilePath,"{\"asioDriverName\":"+value+"}");
        using var service = new AppSettingsService(root.FullName);
        Assert.That(async()=>await service.LoadAsync(CancellationToken.None),Throws.TypeOf<IOException>().With.Message.Contains("asioDriverName"));
    }

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

    [Test]
    public async Task SeedsMissingWindowPlacementWithDefaults()
    {
        await File.WriteAllTextAsync(FilePath, "{\"keep\":17}");
        using var service = new AppSettingsService(root.FullName);

        var settings = await service.LoadAsync(CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(settings.IsMaximized, Is.False);
            Assert.That(settings.RestoredWidth, Is.EqualTo(1280));
            Assert.That(settings.RestoredHeight, Is.EqualTo(720));
        });
        var json = JObject.Parse(await File.ReadAllTextAsync(FilePath));
        Assert.That(json["keep"]!.Value<int>(), Is.EqualTo(17));
        Assert.That(json["isMaximized"]!.Value<bool>(), Is.False);
        Assert.That(json["restoredWidth"]!.Value<double>(), Is.EqualTo(1280));
        Assert.That(json["restoredHeight"]!.Value<double>(), Is.EqualTo(720));
    }

    [Test]
    public async Task SavesWindowPlacementAtomicallyAndPreservesOtherProperties()
    {
        await File.WriteAllTextAsync(FilePath, "{\"osuPath\":\"C:\\\\osu\",\"keep\":17}");
        using var service = new AppSettingsService(root.FullName);

        await service.SaveWindowPlacementAsync(true, 1440, 900, CancellationToken.None);

        var json = JObject.Parse(await File.ReadAllTextAsync(FilePath));
        Assert.That(json["osuPath"]!.Value<string>(), Is.EqualTo(@"C:\osu"));
        Assert.That(json["keep"]!.Value<int>(), Is.EqualTo(17));
        Assert.That(json["isMaximized"]!.Value<bool>(), Is.True);
        Assert.That(json["restoredWidth"]!.Value<double>(), Is.EqualTo(1440));
        Assert.That(json["restoredHeight"]!.Value<double>(), Is.EqualTo(900));
    }

    [TestCase("{\"isMaximized\":17}")]
    [TestCase("{\"restoredWidth\":\"wide\"}")]
    [TestCase("{\"restoredHeight\":true}")]
    public async Task InvalidWindowPlacementTypesArePreserved(string json)
    {
        await File.WriteAllTextAsync(FilePath, json);
        using var service = new AppSettingsService(root.FullName);

        Assert.ThrowsAsync<IOException>(() => service.LoadAsync(CancellationToken.None));
        Assert.That(await File.ReadAllTextAsync(FilePath), Is.EqualTo(json));
    }

    [TestCase(0, 720)]
    [TestCase(-1, 720)]
    [TestCase(double.NaN, 720)]
    [TestCase(1280, 0)]
    [TestCase(1280, double.PositiveInfinity)]
    public void RejectsInvalidWindowPlacementDimensions(double width, double height)
    {
        using var service = new AppSettingsService(root.FullName);

        Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            service.SaveWindowPlacementAsync(false, width, height, CancellationToken.None));
        Assert.That(File.Exists(FilePath), Is.False);
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
