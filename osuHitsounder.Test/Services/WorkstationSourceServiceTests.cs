using Newtonsoft.Json;
using osuHitsounder.Core.Entities;
using osuHitsounder.Services.FileSystem;

namespace osuHitsounder.Test.Services;

public class WorkstationSourceServiceTests
{
    private string root = null!;
    private string songFolder = null!;
    private string projectPath = null!;

    [SetUp]
    public void SetUp()
    {
        root = Directory.CreateTempSubdirectory("osuHitsounder-workstation-").FullName;
        songFolder = Directory.CreateDirectory(Path.Combine(root, "Songs", "Selected")).FullName;
        var projectFolder = Directory.CreateDirectory(Path.Combine(root, "projects", "Test"));
        projectPath = Path.Combine(projectFolder.FullName, "project.json");
        File.WriteAllText(projectPath, JsonConvert.SerializeObject(new ProjectData { BeatmapPath = songFolder }));
    }

    [TearDown]
    public void TearDown() => Directory.Delete(root, true);

    [Test]
    public async Task SelectsOrdinalFirstBeatmapAndReadsQuotedUnicodeAudioFilename()
    {
        File.WriteAllText(Path.Combine(songFolder, "b.osu"), "[General]\nAudioFilename: other.mp3\n");
        File.WriteAllText(Path.Combine(songFolder, "a.osu"), "\uFEFF[General]\nAudioFilename: \"音楽 sample.ogg\"\n[Metadata]\nTitle:x\n");
        File.WriteAllText(Path.Combine(songFolder, "音楽 sample.ogg"), "audio");
        File.WriteAllText(Path.Combine(songFolder, "other.mp3"), "audio");

        var result = await new WorkstationSourceService().LoadAsync(projectPath, CancellationToken.None);

        Assert.That(result.BeatmapPath, Is.EqualTo(Path.Combine(songFolder, "a.osu")));
        Assert.That(result.AudioPath, Is.EqualTo(Path.Combine(songFolder, "音楽 sample.ogg")));
    }

    [Test]
    public void SplitsAudioFilenameAtFirstColon()
    {
        File.WriteAllText(Path.Combine(songFolder, "a.osu"), "[General]\nAudioFilename: name:part.mp3\n");
        var error = Assert.ThrowsAsync<FileNotFoundException>(() =>
            new WorkstationSourceService().LoadAsync(projectPath, CancellationToken.None));
        Assert.That(error!.Message, Does.Contain("name:part.mp3"));
    }

    [TestCase("[Metadata]\nAudioFilename: song.mp3")]
    [TestCase("[General]\nAudioFilename:")]
    [TestCase("[General]\nPreviewTime: 10")]
    public void MissingGeneralAudioFilenameIsRejected(string contents)
    {
        File.WriteAllText(Path.Combine(songFolder, "a.osu"), contents);
        Assert.ThrowsAsync<InvalidDataException>(() =>
            new WorkstationSourceService().LoadAsync(projectPath, CancellationToken.None));
    }

    [TestCase("../outside.mp3")]
    [TestCase("C:\\outside.mp3")]
    public void AudioOutsideSongFolderIsRejected(string audioName)
    {
        File.WriteAllText(Path.Combine(songFolder, "a.osu"), $"[General]\nAudioFilename: {audioName}\n");
        Assert.ThrowsAsync<InvalidDataException>(() =>
            new WorkstationSourceService().LoadAsync(projectPath, CancellationToken.None));
    }

    [Test]
    public void MissingBeatmapOrAudioIsReported()
    {
        Assert.ThrowsAsync<FileNotFoundException>(() =>
            new WorkstationSourceService().LoadAsync(projectPath, CancellationToken.None));
        File.WriteAllText(Path.Combine(songFolder, "a.osu"), "[General]\nAudioFilename: absent.mp3\n");
        Assert.ThrowsAsync<FileNotFoundException>(() =>
            new WorkstationSourceService().LoadAsync(projectPath, CancellationToken.None));
    }

    [Test]
    public void InvalidProjectAndCancellationAreObserved()
    {
        File.WriteAllText(projectPath, "broken");
        Assert.CatchAsync<JsonException>(() =>
            new WorkstationSourceService().LoadAsync(projectPath, CancellationToken.None));
        Assert.ThrowsAsync<TaskCanceledException>(() =>
            new WorkstationSourceService().LoadAsync(projectPath, new CancellationToken(true)));
    }
}
