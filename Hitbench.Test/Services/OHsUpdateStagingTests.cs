using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using Hitbench.Resources.DataModels;
using Hitbench.Services.FileSystem;

namespace Hitbench.Test.Services;

public sealed class OHsUpdateStagingTests
{
    private string root = null!;
    [SetUp] public void SetUp() => root = Directory.CreateTempSubdirectory("hitbench-stage-test-").FullName;
    [TearDown] public void TearDown() => Directory.Delete(root, true);
    [Test]
    public async Task DownloadsVerifiedPayloadWithoutTouchingInstallation()
    {
        var bytes = OHsUpdateTestData.Archive();
        using var http = Client(bytes);
        var values = new List<double>();
        var progress = new InlineProgress<double>(values.Add);
        var staged = await new OHsUpdateService(http, root, root, "1.2.3", OHsUpdateTestData.Commit).StageAsync(
            OHsUpdateTestData.Candidate(bytes.Length, "sha256:" + Convert.ToHexString(SHA256.HashData(bytes))), progress, default);
        Assert.That(File.Exists(Path.Combine(staged.PayloadDirectory, "Hitbench.exe")), Is.True);
        Assert.That(File.Exists(Path.Combine(root, "Hitbench.exe")), Is.False);
        Assert.That(values[^1], Is.EqualTo(100));
        Assert.That(values, Is.Ordered);
    }
    [TestCase("../escaped")] [TestCase("/rooted")] [TestCase("C:/absolute")]
    [TestCase("appsettings.json")] [TestCase("data.json")] [TestCase("projects/a/file")]
    [TestCase("saved/sample.wav")] [TestCase("Hitbench.exe:evil")] [TestCase("alias. /evil")]
    [TestCase("HITBENCH.EXE")]
    public void RejectsUnsafeEntries(string entry)
    {
        var bytes = OHsUpdateTestData.Archive(extra: entry);
        using var http = Client(bytes);
        Assert.ThrowsAsync<InvalidDataException>(() => new OHsUpdateService(http, root, root, "1.2.3", OHsUpdateTestData.Commit)
            .StageAsync(OHsUpdateTestData.Candidate(bytes.Length), new InlineProgress<double>(_ => { }), default));
        Assert.That(Directory.GetFileSystemEntries(root), Is.Empty);
    }
    [TestCase(1, null, false)] [TestCase(0, "sha256:0000", false)] [TestCase(0, null, true)]
    public void RejectsSizeDigestAndIdentityMismatch(int extraSize, string? digest, bool wrongVersion)
    {
        var bytes = OHsUpdateTestData.Archive(version: wrongVersion ? "9.9.9" : "1.2.4-beta");
        using var http = Client(bytes);
        Assert.ThrowsAsync<InvalidDataException>(() => new OHsUpdateService(http, root, root, "1.2.3", OHsUpdateTestData.Commit)
            .StageAsync(OHsUpdateTestData.Candidate(bytes.Length + extraSize, digest), new InlineProgress<double>(_ => { }), default));
        Assert.That(Directory.GetFileSystemEntries(root), Is.Empty);
    }
    [Test]
    public void CancellationAfterFirstBytesRemovesPartialDownload()
    {
        var bytes = OHsUpdateTestData.Archive();
        using var http = Client(bytes);
        using var cancellation = new CancellationTokenSource();
        var values = new List<double>();
        Assert.CatchAsync<OperationCanceledException>(() => new OHsUpdateService(http, root, root, "1.2.3", OHsUpdateTestData.Commit)
            .StageAsync(OHsUpdateTestData.Candidate(bytes.Length), new InlineProgress<double>(value => { values.Add(value); cancellation.Cancel(); }), cancellation.Token));
        Assert.That(values, Does.Not.Contain(100));
        Assert.That(Directory.GetFileSystemEntries(root), Is.Empty);
    }
    [Test]
    public async Task ReleasePackageFixturePassesRealStagingValidation()
    {
        var package = Environment.GetEnvironmentVariable("HITBENCH_TEST_UPDATE_ARCHIVE");
        var version = package is null ? "1.2.4-beta" : "9.8.7-beta";
        var commit = package is null ? OHsUpdateTestData.Commit : Environment.GetEnvironmentVariable("HITBENCH_TEST_UPDATE_COMMIT")!;
        var bytes = package is null ? OHsUpdateTestData.Archive() : await File.ReadAllBytesAsync(package);
        using var http = Client(bytes);
        OHsReleaseVersion.TryParse(version, out var parsed);
        var release = OHsUpdateTestData.Candidate(bytes.Length) with { Version = parsed!, SourceCommit = commit,
            AssetUri = new($"https://github.com/Hokichi/osuHitsounder/releases/download/{version}/Hitbench-{version}-win-x64.zip") };
        var service = new OHsUpdateService(http, root, root, "1.2.3", OHsUpdateTestData.Commit);
        var staged = await service.StageAsync(release, new InlineProgress<double>(_ => { }), default);
        Assert.That(staged.Version, Is.EqualTo(version));
        Assert.That(staged.Files, Does.Contain("Hitbench.exe"));
        Assert.That(staged.Files, Does.Not.Contain("appsettings.json"));
        service.Discard(staged);
        Assert.That(Directory.Exists(staged.Workspace), Is.False);
    }
    [Test]
    public void RejectsWrongCommitMissingApplicationAndLinkedEntries()
    {
        foreach (var bytes in new[] { OHsUpdateTestData.Archive(commit: new string('b', 40)), OHsUpdateTestData.Archive(missing: "Hitbench.exe"), OHsUpdateTestData.Archive(linked: true), new byte[100] })
        {
            using var http = Client(bytes);
            Assert.ThrowsAsync<InvalidDataException>(() => new OHsUpdateService(http, root, root, "1.2.3", OHsUpdateTestData.Commit)
                .StageAsync(OHsUpdateTestData.Candidate(bytes.Length), new InlineProgress<double>(_ => { }), default));
            Assert.That(Directory.GetFileSystemEntries(root), Is.Empty);
        }
    }
    [Test]
    public async Task UnicodeAndApostropheWorkspacePathsArePreserved()
    {
        var temporary = Directory.CreateDirectory(Path.Combine(root, "ไทย's folder")).FullName;
        var bytes = OHsUpdateTestData.Archive();
        using var http = Client(bytes);
        var service = new OHsUpdateService(http, root, temporary, "1.2.3", OHsUpdateTestData.Commit);
        var staged = await service.StageAsync(OHsUpdateTestData.Candidate(bytes.Length), new InlineProgress<double>(_ => { }), default);
        Assert.That(File.Exists(Path.Combine(staged.PayloadDirectory, "Hitbench.exe")), Is.True);
        service.Discard(staged);
        Assert.That(Directory.GetFileSystemEntries(temporary), Is.Empty);
    }
    [Test]
    public void CancellationLeavesNoStaging()
    {
        using var http = Client(OHsUpdateTestData.Archive());
        Assert.CatchAsync<OperationCanceledException>(() => new OHsUpdateService(http, root, root, "1.2.3", OHsUpdateTestData.Commit)
            .StageAsync(OHsUpdateTestData.Candidate(100), new InlineProgress<double>(_ => { }), new CancellationToken(true)));
        Assert.That(Directory.GetFileSystemEntries(root), Is.Empty);
    }
    private static HttpClient Client(byte[] bytes) => new(new OHsUpdateHttpHandler((_, _) =>
        Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) })));
}
