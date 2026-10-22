using Newtonsoft.Json.Linq;
using Soundie.Services.FileSystem;
namespace Soundie.Test.Services;
public sealed class AppSettingsThemeTests
{
    [TestCase("null",0)] [TestCase("\"bad\"",0)] [TestCase("1.5",0)] [TestCase("-1",0)] [TestCase("2",0)] [TestCase("1",1)] [TestCase("0",0)]
    public async Task NormalizesThemeAndPreservesOtherFields(string value, int expected)
    {
        var directory = Directory.CreateTempSubdirectory();
        try {
            var path = Path.Combine(directory.FullName,"appsettings.json");
            await File.WriteAllTextAsync(path,"{\"theme\":"+value+",\"keep\":17}");
            using var service = new AppSettingsService(directory.FullName);
            Assert.That((await service.LoadAsync(default)).Theme, Is.EqualTo(expected));
            await Task.WhenAll(service.SaveThemeAsync(1,default),service.SaveVolumeAsync(37,default));
            var json=JObject.Parse(await File.ReadAllTextAsync(path));
            Assert.That(json["theme"]!.Value<int>(),Is.EqualTo(1));
            Assert.That(json["volume"]!.Value<int>(),Is.EqualTo(37));
            Assert.That(json["keep"]!.Value<int>(),Is.EqualTo(17));
            Assert.ThrowsAsync<ArgumentOutOfRangeException>(()=>service.SaveThemeAsync(2,default));
        } finally { directory.Delete(true); }
    }
}
