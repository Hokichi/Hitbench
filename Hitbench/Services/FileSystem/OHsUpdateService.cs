using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using Hitbench.Resources.DataModels;

namespace Hitbench.Services.FileSystem;

public sealed partial class OHsUpdateService(HttpClient http, string applicationDirectory, string temporaryDirectory,
    string? installedTag, string? installedCommit) : IOHsUpdateService
{
    private const string ApiRoot = "https://api.github.com/repos/Hokichi/osuHitsounder/";
    internal static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public async Task<OHsUpdateRelease?> CheckAsync(CancellationToken token)
    {
        if (!OHsReleaseVersion.TryParse(installedTag, out var installed) || !IsCommit(installedCommit)) return null;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));
        var releases = new List<JsonElement>();
        string? next = ApiRoot + "releases?per_page=100&page=1";
        var visited = new HashSet<string>(StringComparer.Ordinal);
        while (next is not null)
        {
            if (!next.StartsWith(ApiRoot + "releases?", StringComparison.Ordinal) || !visited.Add(next) || visited.Count > 100)
                throw new InvalidDataException("Invalid GitHub pagination.");
            using var request = Request(new(next));
            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            using var data = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false), cancellationToken: timeout.Token).ConfigureAwait(false);
            foreach (var item in data.RootElement.EnumerateArray()) releases.Add(item.Clone());
            next = null;
            if (response.Headers.TryGetValues("Link", out var links))
                foreach (var link in links.SelectMany(value => value.Split(',')))
                {
                    var match = Regex.Match(link, "<([^>]+)>;\\s*rel=\"next\"");
                    if (match.Success) next = match.Groups[1].Value;
                }
        }
        DateTimeOffset? installedPublished = null;
        foreach (var item in releases)
            if (Text(item, "tag_name") == installedTag && !Flag(item, "draft") && DateTimeOffset.TryParse(Text(item, "published_at"), out var published))
                installedPublished = published;
        OHsUpdateRelease? best = null;
        foreach (var item in releases)
        {
            if (Flag(item, "draft") || !OHsReleaseVersion.TryParse(Text(item, "tag_name"), out var version) || version.Tag == installedTag ||
                !DateTimeOffset.TryParse(Text(item, "published_at"), out var published) || !item.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array) continue;
            var comparison = version.NumericVersion.CompareTo(installed.NumericVersion);
            if (comparison < 0 || comparison == 0 && (installedPublished is null || published <= installedPublished)) continue;
            foreach (var asset in assets.EnumerateArray())
            {
                var expected = $"Hitbench-{version.Tag}-win-x64.zip";
                if (Text(asset, "name") != expected || !asset.TryGetProperty("size", out var sizeValue) || !sizeValue.TryGetInt64(out var size) || size <= 0 ||
                    !Uri.TryCreate(Text(asset, "browser_download_url"), UriKind.Absolute, out var uri) || !IsAssetUri(uri, version.Tag)) continue;
                var id = item.TryGetProperty("id", out var idValue) && idValue.TryGetInt64(out var number) ? number : 0;
                var candidate = new OHsUpdateRelease(version, published, id, "", uri, size, Text(asset, "digest"));
                if (best is null || version.NumericVersion > best.Version.NumericVersion || version.NumericVersion == best.Version.NumericVersion &&
                    (published > best.PublishedAt || published == best.PublishedAt && id > best.ReleaseId)) best = candidate;
            }
        }
        if (best is null) return null;
        var target = await ReadJsonAsync(ApiRoot + "git/ref/tags/" + Uri.EscapeDataString(best.Version.Tag), timeout.Token).ConfigureAwait(false);
        for (var depth = 0; depth < 8; depth++)
        {
            if (!target.TryGetProperty("object", out var reference) || !IsCommit(Text(reference, "sha"))) break;
            var sha = Text(reference, "sha")!;
            if (Text(reference, "type") == "commit") return best with { SourceCommit = sha };
            if (Text(reference, "type") != "tag") break;
            target = await ReadJsonAsync(ApiRoot + "git/tags/" + sha, timeout.Token).ConfigureAwait(false);
        }
        throw new InvalidDataException("Release tag does not resolve to a commit.");
    }

    private async Task<JsonElement> ReadJsonAsync(string uri, CancellationToken token)
    {
        using var request = Request(new(uri));
        using var response = await http.SendAsync(request, token).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        using var document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false), cancellationToken: token).ConfigureAwait(false);
        return document.RootElement.Clone();
    }
    private static HttpRequestMessage Request(Uri uri)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.UserAgent.ParseAdd("Hitbench-Updater/1.0");
        request.Headers.Accept.ParseAdd("application/vnd.github+json");
        return request;
    }
    private static string? Text(JsonElement item, string name) => item.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    private static bool Flag(JsonElement item, string name) => item.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.True;
    private static bool IsCommit(string? value) => value is not null && Regex.IsMatch(value, "\\A[0-9a-fA-F]{40,64}\\z");
    private static bool IsAssetUri(Uri uri, string tag) => uri.Scheme == "https" && uri.Host == "github.com" && uri.IsDefaultPort &&
        uri.UserInfo.Length == 0 && uri.Query.Length == 0 && uri.Fragment.Length == 0 &&
        Uri.UnescapeDataString(uri.AbsolutePath) == $"/Hokichi/osuHitsounder/releases/download/{tag}/Hitbench-{tag}-win-x64.zip";
}
