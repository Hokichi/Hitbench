using System.Net;
using System.Net.Http;
using Hitbench.Services.FileSystem;

namespace Hitbench.Test.Services;

public sealed class OHsUpdateDiscoveryTests
{
    [Test]
    public async Task SamePublicationTimeUsesReleaseIdAsDeterministicTieBreak()
    {
        using var http = Client([OHsUpdateTestData.Release("1.2.4-beta", "2026-11-03T00:00:00Z", 5), OHsUpdateTestData.Release("1.2.4-hotfix", "2026-11-03T00:00:00Z", 6)]);
        Assert.That((await new OHsUpdateService(http, "unused", Path.GetTempPath(), "1.2.3", OHsUpdateTestData.Commit).CheckAsync(default))!.Version.Tag, Is.EqualTo("1.2.4-hotfix"));
    }
    [TestCase(false)] [TestCase(true)]
    public async Task ResolvesAnnotatedTagsButRejectsCycles(bool cycle)
    {
        var tagReads = 0;
        using var http = new HttpClient(new OHsUpdateHttpHandler((request, _) =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.Contains("/git/"))
            {
                tagReads++;
                return Task.FromResult(OHsUpdateTestData.Json(new { @object = new { type = path.Contains("/git/ref/") || cycle ? "tag" : "commit", sha = OHsUpdateTestData.Commit } }));
            }
            return Task.FromResult(OHsUpdateTestData.Json(new[] { OHsUpdateTestData.Release("1.2.4-beta", "2026-11-03T00:00:00Z") }));
        }));
        var service = new OHsUpdateService(http, "unused", Path.GetTempPath(), "1.2.3", OHsUpdateTestData.Commit);
        if (cycle) Assert.ThrowsAsync<InvalidDataException>(() => service.CheckAsync(default));
        else Assert.That((await service.CheckAsync(default))!.SourceCommit, Is.EqualTo(OHsUpdateTestData.Commit));
        Assert.That(tagReads, Is.LessThanOrEqualTo(9));
    }
    [Test]
    public void MalformedResponseAndCancellationAreOwnedByCaller()
    {
        using var http = new HttpClient(new OHsUpdateHttpHandler((_, token) =>
        {
            token.ThrowIfCancellationRequested();
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("invalid json") });
        }));
        var service = new OHsUpdateService(http, "unused", Path.GetTempPath(), "1.2.3", OHsUpdateTestData.Commit);
        Assert.CatchAsync<System.Text.Json.JsonException>(() => service.CheckAsync(default));
        Assert.CatchAsync<OperationCanceledException>(() => service.CheckAsync(new CancellationToken(true)));
    }
    [TestCase("1.2.4-beta", "2026-11-02T00:00:00Z", "1.2.4-beta")]
    [TestCase("1.2.3-beta", "2026-11-03T00:00:00Z", "1.2.3-beta")]
    [TestCase("1.2.3-hotfix", "2026-10-01T00:00:00Z", null)]
    [TestCase("1.2.2", "2026-11-03T00:00:00Z", null)]
    [TestCase("1.2.3", "2026-11-03T00:00:00Z", null)]
    public async Task ComparesNumericThenPublicationTime(string tag, string published, string? expected)
    {
        using var http = Client([OHsUpdateTestData.Release("1.2.3", "2026-11-01T00:00:00Z", asset: false), OHsUpdateTestData.Release(tag, published, 2)]);
        var service = new OHsUpdateService(http, "unused", Path.GetTempPath(), "1.2.3", OHsUpdateTestData.Commit);
        Assert.That((await service.CheckAsync(default))?.Version.Tag, Is.EqualTo(expected));
    }
    [TestCase("1.2.3-beta", null)] [TestCase("1.2.4-beta", "1.2.4-beta")]
    public async Task MissingInstalledReleaseOnlyPermitsHigherNumeric(string tag, string? expected)
    {
        using var http = Client([OHsUpdateTestData.Release(tag, "2026-11-03T00:00:00Z")]);
        Assert.That((await new OHsUpdateService(http, "unused", Path.GetTempPath(), "1.2.3", OHsUpdateTestData.Commit).CheckAsync(default))?.Version.Tag, Is.EqualTo(expected));
    }
    [Test]
    public async Task PaginatesAndIgnoresDraftsAndMissingAssets()
    {
        var pages = 0;
        using var http = new HttpClient(new OHsUpdateHttpHandler((request, _) =>
        {
            if (request.RequestUri!.AbsolutePath.Contains("/git/ref/")) return Task.FromResult(OHsUpdateTestData.Json(new { @object = new { type = "commit", sha = OHsUpdateTestData.Commit } }));
            pages++;
            var response = pages == 1 ? OHsUpdateTestData.Json(new[] { OHsUpdateTestData.Release("1.2.4", "2026-11-01T00:00:00Z") }) :
                OHsUpdateTestData.Json(new[] { OHsUpdateTestData.Release("1.3.0-beta", "2026-11-02T00:00:00Z"), OHsUpdateTestData.Release("9.0.0", "2026-11-03T00:00:00Z", draft: true), OHsUpdateTestData.Release("8.0.0", "2026-11-03T00:00:00Z", asset: false) });
            if (pages == 1) response.Headers.Add("Link", "<https://api.github.com/repos/Hokichi/osuHitsounder/releases?per_page=100&page=2>; rel=\"next\"");
            return Task.FromResult(response);
        }));
        var result = await new OHsUpdateService(http, "unused", Path.GetTempPath(), "1.2.3", OHsUpdateTestData.Commit).CheckAsync(default);
        Assert.That(result!.Version.Tag, Is.EqualTo("1.3.0-beta"));
        Assert.That(result.SourceCommit, Is.EqualTo(OHsUpdateTestData.Commit));
        Assert.That(pages, Is.EqualTo(2));
    }
    [Test]
    public async Task UnversionedDevelopmentBuildDoesNotCallNetwork()
    {
        using var http = new HttpClient(new OHsUpdateHttpHandler((_, _) => throw new AssertionException("Unexpected network call.")));
        Assert.That(await new OHsUpdateService(http, "unused", Path.GetTempPath(), null, null).CheckAsync(default), Is.Null);
    }
    [Test]
    public void NetworkFailuresRemainObservableToOwner()
    {
        using var http = new HttpClient(new OHsUpdateHttpHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Forbidden))));
        Assert.ThrowsAsync<HttpRequestException>(() => new OHsUpdateService(http, "unused", Path.GetTempPath(), "1.2.3", OHsUpdateTestData.Commit).CheckAsync(default));
    }
    private static HttpClient Client(object[] releases) => new(new OHsUpdateHttpHandler((request, _) => Task.FromResult(
        request.RequestUri!.AbsolutePath.Contains("/git/ref/") ? OHsUpdateTestData.Json(new { @object = new { type = "commit", sha = OHsUpdateTestData.Commit } }) : OHsUpdateTestData.Json(releases))));
}
