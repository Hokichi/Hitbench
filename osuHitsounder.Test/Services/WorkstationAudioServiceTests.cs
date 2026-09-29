using NAudio.Wave;
using osuHitsounder.Core.Entities;
using osuHitsounder.Services;

namespace osuHitsounder.Test.Services;

public class WorkstationAudioServiceTests
{
    [Test]
    public async Task PreparedPlaybackAppliesSongAndSampleGainIndependently()
    {
        using var project = new TestProjectFolder("hit.wav");
        var player = new StubWavePlayer();
        using var service = new WorkstationAudioService(path =>
            path.EndsWith("hit.wav", StringComparison.OrdinalIgnoreCase)
                ? CreateStream(Enumerable.Repeat(0.4f, 8).ToArray(), 44_100, 2)
                : CreateStream(Enumerable.Repeat(0.2f, 16).ToArray(), 44_100, 2), () => player);
        service.Open("song.wav");
        service.SetSongVolume(50);

        await service.UpdateSamplesAsync(project.ProjectPath,
            [new SamplePlaybackEvent(1, "hit.wav", 0, 50, 50)], CancellationToken.None);
        service.Play();

        var output = new float[2];
        player.Provider!.ToSampleProvider().Read(output, 0, output.Length);

        Assert.That(output, Is.EqualTo(new[] { 0.2f, 0.2f }).Within(0.0001));
    }

    [Test]
    public async Task MasterVolumesUpdateLiveAndSurvivePlaybackRecreation()
    {
        using var project = new TestProjectFolder("hit.wav");
        var players = new List<StubWavePlayer>();
        using var service = new WorkstationAudioService(path =>
            path.EndsWith("hit.wav", StringComparison.OrdinalIgnoreCase)
                ? CreateStream(Enumerable.Repeat(0.4f, 16).ToArray(), 44_100, 2)
                : CreateStream(Enumerable.Repeat(0.2f, 32).ToArray(), 44_100, 2), () =>
            {
                var player = new StubWavePlayer();
                players.Add(player);
                return player;
            });
        service.Open("song.wav");
        service.SetSongVolume(50);
        service.SetSamplesVolume(50);
        service.SetApplicationVolume(50);
        await service.UpdateSamplesAsync(project.ProjectPath,
            [new SamplePlaybackEvent(1, "hit.wav", 0, 50, 100)], CancellationToken.None);

        service.Play();

        var first = new float[2];
        players[^1].Provider!.ToSampleProvider().Read(first, 0, first.Length);
        service.SetSamplesVolume(0);
        var muted = new float[2];
        players[^1].Provider!.ToSampleProvider().Read(muted, 0, muted.Length);
        service.Seek(TimeSpan.Zero);
        var recreated = new float[2];
        players[^1].Provider!.ToSampleProvider().Read(recreated, 0, recreated.Length);

        Assert.Multiple(() =>
        {
            Assert.That(first, Is.EqualTo(new[] { 0.1f, 0.1f }).Within(0.0001));
            Assert.That(muted, Is.EqualTo(new[] { 0.05f, 0.05f }).Within(0.0001));
            Assert.That(recreated, Is.EqualTo(new[] { 0.05f, 0.05f }).Within(0.0001));
            Assert.That(players, Has.Count.EqualTo(2));
        });
    }

    [Test]
    public async Task MutingSongLeavesPreparedSampleAudible()
    {
        using var project = new TestProjectFolder("hit.wav");
        var player = new StubWavePlayer();
        using var service = new WorkstationAudioService(path =>
            path.EndsWith("hit.wav", StringComparison.OrdinalIgnoreCase)
                ? CreateStream(Enumerable.Repeat(0.4f, 8).ToArray(), 44_100, 2)
                : CreateStream(Enumerable.Repeat(0.2f, 16).ToArray(), 44_100, 2), () => player);
        service.Open("song.wav");
        service.SetSongVolume(0);

        await service.UpdateSamplesAsync(project.ProjectPath,
            [new SamplePlaybackEvent(1, "hit.wav", 0, 50, 50)], CancellationToken.None);
        service.Play();

        var output = new float[2];
        player.Provider!.ToSampleProvider().Read(output, 0, output.Length);

        Assert.That(output, Is.EqualTo(new[] { 0.1f, 0.1f }).Within(0.0001));
    }

    [Test]
    public async Task MissingSampleReportsErrorButKeepsSongPlayable()
    {
        using var project = new TestProjectFolder();
        var player = new StubWavePlayer();
        using var service = new WorkstationAudioService(_ => CreateStream(), () => player);
        service.Open("song.wav");

        await service.UpdateSamplesAsync(project.ProjectPath,
            [new SamplePlaybackEvent(1, "missing.wav", 0, 100, 100)], CancellationToken.None);
        service.Play();


        Assert.Multiple(() =>
        {
            Assert.That(service.GetSnapshot().IsPlaying, Is.True);
            Assert.That(service.GetSnapshot().Error, Does.Contain("missing.wav"));
        });
    }

    [Test]
    public async Task OutputPositionUsesPreparedFormatInsteadOfSourceByteRate()
    {
        var player = new StubWavePlayer();
        using var project = new TestProjectFolder();
        using var service = new WorkstationAudioService(_ => CreateStream(new float[2_000], 1_000, 1), () => player);
        service.Open("song.wav");
        await service.UpdateSamplesAsync(project.ProjectPath, [], CancellationToken.None);
        service.Play();

        player.Position = 44_100 * 2 * sizeof(float);

        Assert.That(service.GetSnapshot().Position, Is.EqualTo(TimeSpan.FromSeconds(1)));
    }

    [Test]
    public void PlayBeforePreparationReportsError()
    {
        using var service = new WorkstationAudioService(_ => CreateStream(), () => new StubWavePlayer());
        service.Open("song.wav");

        service.Play();


        Assert.That(service.GetSnapshot().Error, Does.Contain("prepared"));
    }

    [Test]
    public async Task RepeatedSampleReferencesDecodeFileOnce()
    {
        using var project = new TestProjectFolder("hit.wav");
        var sampleDecodes = 0;
        using var service = new WorkstationAudioService(path =>
        {
            if (path.EndsWith("hit.wav", StringComparison.OrdinalIgnoreCase)) sampleDecodes++;
            return path.EndsWith("hit.wav", StringComparison.OrdinalIgnoreCase)
                ? CreateStream([0.4f, 0.4f], 44_100, 2)
                : CreateStream([0.2f, 0.2f], 44_100, 2);
        }, () => new StubWavePlayer());
        service.Open("song.wav");

        await service.UpdateSamplesAsync(project.ProjectPath,
        [
            new SamplePlaybackEvent(1, "hit.wav", 0, 100, 100),
            new SamplePlaybackEvent(2, "hit.wav", 10, 100, 100)
        ], CancellationToken.None);

        Assert.That(sampleDecodes, Is.EqualTo(1));
    }

    [TestCase("../hit.wav")]
    [TestCase("folder/hit.wav")]
    public async Task UnsafeSampleNameIsRejectedWithoutDecoderAccess(string fileName)
    {
        using var project = new TestProjectFolder();
        var unsafeDecode = false;
        using var service = new WorkstationAudioService(path =>
        {
            if (!path.EndsWith("song.wav", StringComparison.OrdinalIgnoreCase)) unsafeDecode = true;
            return CreateStream([0.2f, 0.2f], 44_100, 2);
        }, () => new StubWavePlayer());
        service.Open("song.wav");

        await service.UpdateSamplesAsync(project.ProjectPath,
            [new SamplePlaybackEvent(1, fileName, 0, 100, 100)], CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(unsafeDecode, Is.False);
            Assert.That(service.GetSnapshot().Error, Does.Contain("invalid file name"));
        });
    }

    [TestCase("tone.mp3")]
    [TestCase("tone.ogg")]
    public async Task RealDecoderExtractsPeaksWithoutCreatingOutputDevice(string fileName)
    {
        var createdOutput = false;
        using var service = new WorkstationAudioService(AudioDecoder.Open, () =>
        {
            createdOutput = true;
            return new StubWavePlayer();
        });
        var path = Path.Combine(TestContext.CurrentContext.TestDirectory, "Fixtures", "Audio", fileName);

        var data = await service.AnalyzeAsync(path, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(data.Duration, Is.GreaterThan(TimeSpan.FromMilliseconds(100)));
            Assert.That(data.Peaks.Max(), Is.GreaterThan(0));
            Assert.That(createdOutput, Is.False);
        });
    }

    [Test]
    public async Task PlayPauseResumeAndStopPreserveExpectedState()
    {
        var player = new StubWavePlayer();
        using var service = new WorkstationAudioService(_ => CreateStream(), () => player);
        service.Open("test.wav");
        await PrepareAsync(service);
        service.Play();

        Assert.That(service.GetSnapshot().IsPlaying, Is.True);
        service.Pause();
        Assert.That(service.GetSnapshot().IsPlaying, Is.False);
        service.Play();

        Assert.That(service.GetSnapshot().IsPlaying, Is.True);
        player.Position = player.OutputWaveFormat.AverageBytesPerSecond / 2;
        service.Stop();
        Assert.Multiple(() =>
        {
            Assert.That(service.GetSnapshot().Position, Is.EqualTo(TimeSpan.Zero));
            Assert.That(service.GetSnapshot().IsPlaying, Is.False);
        });
    }

    [Test]
    public async Task NaturalEndAndPlaybackErrorResetPosition()
    {
        var player = new StubWavePlayer();
        using var service = new WorkstationAudioService(_ => CreateStream(), () => player);
        service.Open("test.wav");
        await PrepareAsync(service);
        service.Play();

        player.RaiseStopped(new IOException("device lost"));
        var snapshot = service.GetSnapshot();
        Assert.That(snapshot.Position, Is.EqualTo(TimeSpan.Zero));
        Assert.That(snapshot.Error, Does.Contain("device lost"));
    }

    [Test]
    public async Task NaturalEndBuildsFreshOutputWhenPlaybackRestarts()
    {
        var players = new List<StubWavePlayer>();
        using var service = new WorkstationAudioService(_ => CreateStream(), () =>
        {
            var player = new StubWavePlayer();
            players.Add(player);
            return player;
        });
        service.Open("test.wav");
        await PrepareAsync(service);
        service.Play();


        players[0].RaiseStopped();
        service.Play();


        Assert.Multiple(() =>
        {
            Assert.That(players, Has.Count.EqualTo(2));
            Assert.That(players[0].IsDisposed, Is.True);
            Assert.That(service.GetSnapshot().IsPlaying, Is.True);
        });
    }

    [Test]
    public async Task NaturalEndDisposesOutputWithoutHoldingServiceLock()
    {
        var player = new StubWavePlayer();
        using var service = new WorkstationAudioService(_ => CreateStream(), () => player);
        service.Open("test.wav");
        await PrepareAsync(service);
        service.Play();

        player.Disposing = () => Assert.That(
            Task.Run(service.GetSnapshot).Wait(TimeSpan.FromSeconds(1)), Is.True,
            "Device disposal must not hold the service lock needed by callbacks.");

        player.RaiseStopped();
        service.GetSnapshot();

        Assert.That(player.IsDisposed, Is.True);
    }

    [Test]
    public async Task DeviceFailureDoesNotThrowAndDisposeIsIdempotent()
    {
        var player = new StubWavePlayer { FailInitialization = true };
        var service = new WorkstationAudioService(_ => CreateStream(), () => player);
        service.Open("test.wav");
        await PrepareAsync(service);
        Assert.DoesNotThrow(service.Play);
        Assert.That(service.GetSnapshot().Error, Does.Contain("No device"));
        Assert.DoesNotThrow(service.Dispose);
        Assert.DoesNotThrow(service.Dispose);
        Assert.Multiple(() =>
        {
            Assert.That(player.IsDisposed, Is.True);
            Assert.That(player.HasPlaybackStoppedSubscribers, Is.False);
        });
    }

    [Test]
    public async Task SeekBeforePlayUsesAbsoluteOriginAndClamps()
    {
        var players = new List<StubWavePlayer>();
        using var service = new WorkstationAudioService(_ => CreateStream(), () =>
        {
            var player = new StubWavePlayer();
            players.Add(player);
            return player;
        });
        service.Open("test.wav");
        await PrepareAsync(service);
        service.Seek(TimeSpan.FromSeconds(4));
        Assert.That(service.GetSnapshot().Position, Is.EqualTo(TimeSpan.FromSeconds(4)));
        Assert.That(service.GetSnapshot().IsPlaying, Is.False);
        service.Play();

        players[^1].Position = players[^1].OutputWaveFormat.AverageBytesPerSecond;
        Assert.That(service.GetSnapshot().Position, Is.EqualTo(TimeSpan.FromSeconds(5)));
        service.Seek(TimeSpan.FromSeconds(20));
        Assert.That(service.GetSnapshot().Position, Is.EqualTo(TimeSpan.FromSeconds(10)));
        service.Seek(TimeSpan.FromSeconds(-2));
        Assert.That(service.GetSnapshot().Position, Is.EqualTo(TimeSpan.Zero));
    }

    [Test]
    public async Task SeekPreservesPlayingButLeavesPausedInactive()
    {
        var players = new List<StubWavePlayer>();
        using var service = new WorkstationAudioService(_ => CreateStream(), () =>
        {
            var player = new StubWavePlayer();
            players.Add(player);
            return player;
        });
        service.Open("test.wav");
        await PrepareAsync(service);
        service.Play();

        service.Seek(TimeSpan.FromSeconds(3));
        Assert.That(service.GetSnapshot().IsPlaying, Is.True);
        Assert.That(players, Has.Count.EqualTo(2));
        service.Pause();
        service.Seek(TimeSpan.FromSeconds(7));
        Assert.Multiple(() =>
        {
            Assert.That(service.GetSnapshot().IsPlaying, Is.False);
            Assert.That(service.GetSnapshot().Position, Is.EqualTo(TimeSpan.FromSeconds(7)));
            Assert.That(players, Has.Count.EqualTo(2));
        });
        service.Stop();
        Assert.That(service.GetSnapshot().Position, Is.EqualTo(TimeSpan.Zero));
    }

    [Test]
    public async Task ReopenResetsSeekAndDecoderReadAheadDoesNotChangeSnapshot()
    {
        var player = new StubWavePlayer();
        using var service = new WorkstationAudioService(_ => CreateStream(), () => player);
        service.Open("first.wav");
        await PrepareAsync(service);
        service.Seek(TimeSpan.FromSeconds(4));
        service.Play();

        player.Position = 0;
        Assert.That(service.GetSnapshot().Position, Is.EqualTo(TimeSpan.FromSeconds(4)));
        service.Open("second.wav");
        Assert.That(service.GetSnapshot().Position, Is.EqualTo(TimeSpan.Zero));
    }

    [Test]
    public async Task DelayedStoppedCallbackFromReplacedOutputCannotResetSeek()
    {
        var players = new List<StubWavePlayer>();
        using var service = new WorkstationAudioService(_ => CreateStream(), () =>
        {
            var player = new StubWavePlayer();
            players.Add(player);
            return player;
        });
        service.Open("test.wav");
        await PrepareAsync(service);
        service.Play();

        var retired = players[0];
        service.Seek(TimeSpan.FromSeconds(6));
        retired.RaiseRetiredStopped();
        Assert.That(service.GetSnapshot().Position, Is.EqualTo(TimeSpan.FromSeconds(6)));
        Assert.That(service.GetSnapshot().IsPlaying, Is.True);
    }

    private static WaveStream CreateStream()
    {
        var format = WaveFormat.CreateIeeeFloatWaveFormat(10, 1);
        return new RawSourceWaveStream(new MemoryStream(new byte[format.BlockAlign * 100]), format);
    }

    private static WaveStream CreateStream(float[] samples, int sampleRate, int channels)
    {
        var format = WaveFormat.CreateIeeeFloatWaveFormat(sampleRate, channels);
        var bytes = new byte[samples.Length * sizeof(float)];
        Buffer.BlockCopy(samples, 0, bytes, 0, bytes.Length);
        return new RawSourceWaveStream(new MemoryStream(bytes), format);
    }

    private static async Task PrepareAsync(WorkstationAudioService service)
    {
        using var project = new TestProjectFolder();
        await service.UpdateSamplesAsync(project.ProjectPath, [], CancellationToken.None);
    }

}
