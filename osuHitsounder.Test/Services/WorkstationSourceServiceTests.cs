using Newtonsoft.Json;
using osuHitsounder.Core.Entities;
using osuHitsounder.Services.FileSystem;

namespace osuHitsounder.Test.Services;

public class WorkstationSourceServiceTests
{
    [Test]
    public async Task OpeningEnsuresSavedSamplesWithoutChangingImportedAudio()
    {
        File.WriteAllText(Path.Combine(songFolder, "a.osu"), "[General]\nAudioFilename: song.wav\n[TimingPoints]\n0,500,4,1,0,100,1,0\n");
        File.WriteAllText(Path.Combine(songFolder, "song.wav"), "audio");
        await new WorkstationSourceService().LoadAsync(projectPath, default);
        var saved = Path.Combine(Path.GetDirectoryName(projectPath)!, "Saved Samples");
        Assert.That(Directory.Exists(saved), Is.True);
        File.WriteAllText(Path.Combine(saved, "keep.wav"), "keep");
        await new WorkstationSourceService().LoadAsync(projectPath, default);
        Assert.That(File.ReadAllText(Path.Combine(saved, "keep.wav")), Is.EqualTo("keep"));
        Assert.That(Directory.Exists(Path.Combine(Path.GetDirectoryName(projectPath)!, "Samples")), Is.True);
    }
    [Test]
    public async Task LoadsPersistedTrackFlagsWithoutRewritingProject()
    {
        File.WriteAllText(Path.Combine(songFolder, "a.osu"), "[General]\nAudioFilename: song.wav\n[TimingPoints]\n0,500,4,1,0,100,1,0\n");
        File.WriteAllText(Path.Combine(songFolder, "song.wav"), "audio");
        var json = JsonConvert.SerializeObject(new { BeatmapPath = songFolder,
            Tracks = new[] { new { IsTrackMuted = true, IsTrackSoloed = true } } });
        File.WriteAllText(projectPath, json);
        var result = await new WorkstationSourceService().LoadAsync(projectPath, CancellationToken.None);
        Assert.That(result.Project.Tracks.Single().IsTrackMuted, Is.True);
        Assert.That(result.Project.Tracks.Single().IsTrackSoloed, Is.True);
        Assert.That(File.ReadAllText(projectPath), Is.EqualTo(json));
        Assert.That(result.Project.BPMs, Is.EqualTo(new[] { 120 }));
    }

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
        File.WriteAllText(projectPath, JsonConvert.SerializeObject(new { BeatmapPath = songFolder }));
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
        File.AppendAllText(Path.Combine(songFolder, "a.osu"), "[TimingPoints]\n125,500,4,1,0,100,1,0\n");

        var result = await new WorkstationSourceService().LoadAsync(projectPath, CancellationToken.None);

        Assert.That(result.BeatmapPath, Is.EqualTo(Path.Combine(songFolder, "a.osu")));
        Assert.That(result.Project.Offsets, Is.EqualTo(new[] { 125 }));
        Assert.That(result.Project.BPMs, Is.EqualTo(new[] { 120 }));
        Assert.That(File.ReadAllText(projectPath), Does.Not.Contain("Offsets"));
        Assert.That(Directory.Exists(Path.Combine(Path.GetDirectoryName(projectPath)!, "Samples")), Is.True);
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
        Assert.CatchAsync<InvalidDataException>(() =>
            new WorkstationSourceService().LoadAsync(projectPath, CancellationToken.None));
        Assert.ThrowsAsync<TaskCanceledException>(() =>
            new WorkstationSourceService().LoadAsync(projectPath, new CancellationToken(true)));
    }
}
