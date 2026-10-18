using Newtonsoft.Json.Linq;
using osuHitsounder.Services.FileSystem;

namespace osuHitsounder.Test.Services;

public sealed class AppSettingsVolumeTests
{
    [Test]
    public async Task ConcurrentWritesPreserveUnrelatedKeys()
    {
        var directory = Directory.CreateTempSubdirectory();
        try
        {
            var path = Path.Combine(directory.FullName, "appsettings.json");
            await File.WriteAllTextAsync(path, "{\"keep\":17}");
            using var service = new AppSettingsService(directory.FullName);
            await Task.WhenAll(service.SaveVolumeAsync(37, default),
                service.SaveOsuPathAsync(directory.FullName, default),
                service.SaveWindowPlacementAsync(true, 1440, 900, default));
            var json = JObject.Parse(await File.ReadAllTextAsync(path));
            Assert.Multiple(() =>
            {
                Assert.That(json["volume"]!.Value<int>(), Is.EqualTo(37));
                Assert.That(json["keep"]!.Value<int>(), Is.EqualTo(17));
                Assert.That(json["osuPath"]!.Value<string>(), Is.EqualTo(directory.FullName));
                Assert.That(json["isMaximized"]!.Value<bool>(), Is.True);
            });
        }
        finally { directory.Delete(true); }
    }

    [TestCase("true")]
    [TestCase("\"30\"")]
    [TestCase("[]")]
    [TestCase("NaN")]
    public async Task RejectsInvalidVolume(string value)
    {
        var directory = Directory.CreateTempSubdirectory();
        try
        {
            await File.WriteAllTextAsync(Path.Combine(directory.FullName, "appsettings.json"), "{\"volume\":" + value + "}");
            using var service = new AppSettingsService(directory.FullName);
            Assert.ThrowsAsync<IOException>(() => service.LoadAsync(default));
        }
        finally { directory.Delete(true); }
    }
    [TestCase("{}", 100)]
    [TestCase("{\"volume\":null}", 100)]
    [TestCase("{\"volume\":42.9}", 42)]
    [TestCase("{\"volume\":-10}", 0)]
    [TestCase("{\"volume\":1000}", 100)]
    public async Task LoadNormalizesVolume(string json, int expected)
    {
        var directory = Directory.CreateTempSubdirectory();
        try
        {
            var path = Path.Combine(directory.FullName, "appsettings.json");
            await File.WriteAllTextAsync(path, json);
            using var service = new AppSettingsService(directory.FullName);
            await service.LoadAsync(default);
            Assert.That(JObject.Parse(await File.ReadAllTextAsync(path))["volume"]?.Value<int>(), Is.EqualTo(expected));
        }
        finally { directory.Delete(true); }
    }
}
