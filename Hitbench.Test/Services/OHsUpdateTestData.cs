using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using Hitbench.Resources.DataModels;

namespace Hitbench.Test.Services;

public static class OHsUpdateTestData
{
    public const string Commit = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    public static HttpResponseMessage Json(object value) => new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(value)) };
    public static object Release(string version, string published, long id = 1, bool draft = false, bool asset = true, string? assetUrl = null) => new
    {
        tag_name = version, published_at = published, id, draft, prerelease = version.Contains("beta"),
        assets = asset ? new[] { new { name = $"Hitbench-{version}-win-x64.zip", size = 100,
            browser_download_url = assetUrl ?? $"https://github.com/Hokichi/Hitbench/releases/download/{version}/Hitbench-{version}-win-x64.zip", digest = (string?)null } } : [],
    };
    public static OHsUpdateRelease Candidate(long size, string? digest = null)
    {
        OHsReleaseVersion.TryParse("1.2.4-beta", out var version);
        return new(version!, DateTimeOffset.Parse("2026-11-03T01:00:00Z"), 2, Commit,
            new("https://github.com/Hokichi/Hitbench/releases/download/1.2.4-beta/Hitbench-1.2.4-beta-win-x64.zip"), size, digest);
    }
    public static byte[] Archive(string version = "1.2.4-beta", string commit = Commit, string? extra = null, string? missing = null, bool linked = false, bool singleFile = false)
    {
        var entries = new Dictionary<string, string>
        { ["Hitbench.exe"] = "executable", ["Hitbench.dll"] = "assembly", ["Hitbench.runtimeconfig.json"] = "{}", ["Hitbench.deps.json"] = "{}" };
        if (singleFile)
            foreach (var file in new[] { "Hitbench.dll", "Hitbench.runtimeconfig.json", "Hitbench.deps.json" }) entries.Remove(file);
        if (extra is not null) entries[extra] = "invalid";
        if (missing is not null) entries.Remove(missing);
        entries["hitbench-release.json"] = JsonSerializer.Serialize(new { Version = version, Commit = commit, Files = entries.Keys.Append("hitbench-release.json").ToArray() });
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, true))
            foreach (var entry in entries)
            {
                var created = archive.CreateEntry(entry.Key);
                if (linked && entry.Key == "Hitbench.exe") created.ExternalAttributes = unchecked((int)0xA0000000);
                using var writer = new StreamWriter(created.Open(), Encoding.UTF8);
                writer.Write(entry.Value);
            }
        return stream.ToArray();
    }
}
