using osuHitsounder.Services.FileSystem;

namespace osuHitsounder.Test.Services;

public class OsuExecutableEnumeratorTests
{
    [Test]
    public void StreamingScanObservesCancellationBeforeLaterMatches()
    {
        var root = Directory.CreateTempSubdirectory("osuHitsounder-enumerator-");
        try
        {
            File.WriteAllText(Path.Combine(root.FullName, "osu!.exe"), "");
            var later = root.CreateSubdirectory("later");
            File.WriteAllText(Path.Combine(later.FullName, "osu!.exe"), "");
            using var cancellation = new CancellationTokenSource();
            using var entries = new OsuExecutableEnumerator(root.FullName, cancellation.Token, () => { });
            Assert.That(entries.MoveNext(), Is.True);
            Assert.That(entries.Current, Is.EqualTo(Path.Combine(root.FullName, "osu!.exe")));
            cancellation.Cancel();
            Assert.Catch<OperationCanceledException>(() => entries.MoveNext());
        }
        finally { root.Delete(true); }
    }

    [Test]
    public async Task AbsentRootIsReportedAsSkippedCoverage()
    {
        var root = Directory.CreateTempSubdirectory("osuHitsounder-missing-");
        try
        {
            var data = await new HomeFolderService(root.FullName, () => [Path.Combine(root.FullName, "missing")])
                .LoadAsync(CancellationToken.None);
            Assert.That(data.OsuDirectory, Is.Null);
            Assert.That(data.SkippedLocations, Is.True);
        }
        finally { root.Delete(true); }
    }
}
