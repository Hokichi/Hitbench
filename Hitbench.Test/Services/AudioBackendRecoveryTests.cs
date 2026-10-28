using NAudio.Wave;
using Hitbench.Services;

namespace Hitbench.Test.Services;

public class AudioBackendRecoveryTests
{
    [Test]
    public async Task PlayFailureAndDisposeFailureStillTryFallback()
    {
        using var project = new TestProjectFolder();
        var failed = new StubWavePlayer { FailPlayback = true, FailStop = true, Disposing = () => throw new IOException("dispose failed") };
        var fallback = new StubWavePlayer();
        using var service = new WorkstationAudioService(_ => Signal(),
            [Candidate("WASAPI", () => failed), Candidate("WaveOut", () => fallback)]);
        service.Open("song.wav");
        await service.UpdateSamplesAsync(project.ProjectPath, [], default);
        Assert.DoesNotThrow(service.Play);
        Assert.That(service.GetSnapshot().IsPlaying, Is.True);
        Assert.That(service.GetSnapshot().Warning, Does.Contain("Cannot start device"));
    }

    [Test]
    public async Task PolledStopCleanupFailureDoesNotEscapeTransport()
    {
        using var project = new TestProjectFolder();
        var player = new StubWavePlayer { Disposing = () => throw new IOException("dispose failed") };
        using var service = new WorkstationAudioService(_ => Signal(), [Candidate("WASAPI", () => player)]);
        service.Open("song.wav");
        await service.UpdateSamplesAsync(project.ProjectPath, [], default);
        service.Play();
        player.RaiseStopped(new IOException("original"));
        Assert.DoesNotThrow(() => service.GetSnapshot());
        Assert.That(service.GetSnapshot().Error, Does.Contain("original"));
    }

    [Test]
    public async Task AsynchronousStartupFailureDoesNotStartFallbackUntilExplicitPlay()
    {
        using var project = new TestProjectFolder();
        var failed = new StubWavePlayer();
        failed.Playing = () => failed.RaiseStopped(new IOException("startup failed"));
        var fallbacks = 0;
        using var service = new WorkstationAudioService(_ => Signal(),
            [Candidate("WASAPI", () => failed), Candidate("WaveOut", () => { fallbacks++; return new StubWavePlayer(); })]);
        service.Open("song.wav");
        await service.UpdateSamplesAsync(project.ProjectPath, [], CancellationToken.None);
        service.Play();
        var snapshot = service.GetSnapshot();
        Assert.That(snapshot.IsPlaying, Is.False);
        Assert.That(snapshot.Error, Does.Contain("WASAPI").And.Contain("startup failed"));
        Assert.That(fallbacks, Is.Zero);
        service.Play();
        Assert.That(fallbacks, Is.EqualTo(1));
        Assert.That(service.GetSnapshot().IsPlaying, Is.True);
        failed.RaiseRetiredStopped(new IOException("late"));
        Assert.That(service.GetSnapshot().Error, Is.Null);
    }

    [Test]
    public async Task CandidateInputsAreValidatedAndCopied()
    {
        Assert.Throws<ArgumentException>(() => new WorkstationAudioService(_ => Signal(), Array.Empty<AudioOutputCandidate>()));
        Assert.Throws<ArgumentException>(() => new WorkstationAudioService(_ => Signal(), [new("", () => new AudioOutputDevice(() => new StubWavePlayer()))]));
        using var project = new TestProjectFolder();
        var player = new StubWavePlayer();
        var candidates = new[] { Candidate("WASAPI", () => player) };
        using var service = new WorkstationAudioService(_ => Signal(), candidates);
        candidates[0] = new("changed", () => throw new IOException("mutated input"));
        service.Open("song.wav");
        await service.UpdateSamplesAsync(project.ProjectPath, [], CancellationToken.None);
        service.Play();
        Assert.That(service.GetSnapshot().IsPlaying, Is.True);
        Assert.That(player.Provider, Is.Not.Null);
    }

    [TestCase(false, 0, "create")]
    [TestCase(false, 1, "init")]
    [TestCase(false, 2, "play")]
    [TestCase(true, 0, "create")]
    [TestCase(true, 1, "init")]
    [TestCase(true, 2, "play")]
    [TestCase(true, 3, "create")]
    [TestCase(true, 3, "init")]
    [TestCase(true, 3, "play")]
    public async Task SelectionAttemptsEachBackendOnceAndReportsActualOutcome(bool asio, int failures, string stage)
    {
        using var project = new TestProjectFolder();
        var names = asio ? new[] { "ASIO", "WASAPI", "WaveOut" } : new[] { "WASAPI", "WaveOut" };
        var attempts = new List<string>();
        var players = new List<StubWavePlayer>();
        IWavePlayer Create(string name)
        {
            var index = Array.IndexOf(names, name);
            attempts.Add(name);
            if (index < failures && stage == "create") throw new IOException(name + " unavailable");
            var player = new StubWavePlayer
            {
                FailInitialization = index < failures && stage == "init",
                FailPlayback = index < failures && stage == "play"
            };
            players.Add(player);
            return player;
        }
        var factory = new AudioOutputFactory(_ => Create("ASIO"), () => Create("WASAPI"), () => Create("WaveOut"));
        var candidates = factory.CreateCandidates(asio ? "Studio ASIO" : null);
        using var service = new WorkstationAudioService(_ => Signal(), candidates);
        service.Open("song.wav");
        await service.UpdateSamplesAsync(project.ProjectPath, [], CancellationToken.None);
        service.Play();
        var snapshot = service.GetSnapshot();
        var success = failures < names.Length;
        Assert.That(attempts, Is.EqualTo(names.Take(Math.Min(failures + 1, names.Length))));
        Assert.That(snapshot.IsPlaying, Is.EqualTo(success));
        foreach (var retired in players.Take(success ? players.Count - 1 : players.Count))
        {
            Assert.That(retired.IsDisposed, Is.True);
            Assert.That(retired.HasPlaybackStoppedSubscribers, Is.False);
        }
        if (!success)
        {
            foreach (var name in names) Assert.That(snapshot.Error, Does.Contain(name));
        }
        else
        {
            Assert.That(snapshot.Error, Is.Null);
            if (failures == 0) Assert.That(snapshot.Warning, Is.Null);
            else
            {
                Assert.That(snapshot.Warning, Does.Contain(names[failures]));
                foreach (var name in names.Take(failures)) Assert.That(snapshot.Warning, Does.Contain(name));
            }
        }
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task ReadAheadFailurePreservesSeekOrigin(bool duringPlay)
    {
        using var project = new TestProjectFolder();
        var failed = new StubWavePlayer();
        void ConsumeAndFail(IWaveProvider provider)
        {
            provider.Read(new byte[800], 0, 800);
            throw new IOException("read ahead");
        }
        if (duringPlay) failed.Playing = () => ConsumeAndFail(failed.Provider!);
        else failed.Initializing = ConsumeAndFail;
        var fallback = new StubWavePlayer();
        using var service = new WorkstationAudioService(_ => Signal(),
            [Candidate("WASAPI", () => failed), Candidate("WaveOut", () => fallback)]);
        service.Open("song.wav");
        await service.UpdateSamplesAsync(project.ProjectPath, [], CancellationToken.None);
        service.Seek(TimeSpan.FromMilliseconds(250));
        service.Play();
        var samples = new float[6];
        Assert.That(fallback.Provider!.ToSampleProvider().Read(samples, 0, 6), Is.EqualTo(6));
        Assert.That(samples, Is.EqualTo(Data().Skip(22050).Take(6)));
        Assert.That(service.GetSnapshot().Position, Is.EqualTo(TimeSpan.FromMilliseconds(250)));
        Assert.That(failed.IsDisposed, Is.True);
    }

    [Test]
    public async Task RuntimeFailuresWaitForExplicitPlayAndFallbackSticksUntilOpen()
    {
        using var project = new TestProjectFolder();
        var attempts = new List<string>();
        var players = new List<StubWavePlayer>();
        AudioOutputCandidate Make(string name) => Candidate(name, () =>
        {
            attempts.Add(name);
            var player = new StubWavePlayer();
            players.Add(player);
            return player;
        });
        using var service = new WorkstationAudioService(_ => Signal(), [Make("ASIO"), Make("WASAPI"), Make("WaveOut")]);
        service.Open("song.wav");
        await service.UpdateSamplesAsync(project.ProjectPath, [], CancellationToken.None);
        service.Play();
        players[^1].RaiseStopped(new IOException("driver lost"));
        Assert.That(service.GetSnapshot().IsPlaying, Is.False);
        Assert.That(attempts, Is.EqualTo(new[] { "ASIO" }));
        service.Play();
        Assert.That(attempts[^1], Is.EqualTo("WASAPI"));
        Assert.That(service.GetSnapshot().Warning, Does.Contain("ASIO").And.Contain("WASAPI"));
        service.Pause();
        service.Play();
        service.Seek(TimeSpan.FromMilliseconds(100));
        service.Stop();
        service.Play();
        players[^1].RaiseStopped();
        service.GetSnapshot();
        service.Play();
        Assert.That(attempts.Skip(1), Is.All.EqualTo("WASAPI"));
        Assert.That(service.GetSnapshot().Warning, Does.Contain("WASAPI"));
        players[^1].RaiseStopped(new IOException("device removed"));
        service.GetSnapshot();
        service.Play();
        Assert.That(attempts[^1], Is.EqualTo("WaveOut"));
        var count = attempts.Count;
        players[^1].RaiseStopped(new IOException("wave lost"));
        service.GetSnapshot();
        Assert.That(attempts.Count, Is.EqualTo(count));
        service.Play();
        Assert.That(attempts.Count, Is.EqualTo(count + 1));
        Assert.That(attempts[^1], Is.EqualTo("WaveOut"));
        service.Open("new.wav");
        await service.UpdateSamplesAsync(project.ProjectPath, [], CancellationToken.None);
        service.Play();
        Assert.That(attempts[^1], Is.EqualTo("ASIO"));
        Assert.That(service.GetSnapshot().Warning, Is.Null);
    }

    [TestCase(false, false)]
    [TestCase(false, true)]
    [TestCase(true, false)]
    [TestCase(true, true)]
    public async Task SupersededSelectionCannotStartAnotherCandidate(bool dispose, bool duringCleanup)
    {
        using var project = new TestProjectFolder();
        var failed = new StubWavePlayer();
        var fallbacks = 0;
        using var service = new WorkstationAudioService(_ => Signal(),
            [Candidate("WASAPI", () => failed), Candidate("WaveOut", () => { fallbacks++; return new StubWavePlayer(); })]);
        void Supersede() { if (dispose) service.Dispose(); else service.Open("new.wav"); }
        if (duringCleanup)
        {
            failed.FailInitialization = true;
            failed.Disposing = Supersede;
        }
        else failed.Initializing = _ => { Supersede(); throw new IOException("old failure"); };
        service.Open("song.wav");
        await service.UpdateSamplesAsync(project.ProjectPath, [], CancellationToken.None);
        Assert.DoesNotThrow(service.Play);
        Assert.That(fallbacks, Is.Zero);
        Assert.That(failed.IsDisposed, Is.True);
        failed.RaiseRetiredStopped(new IOException("late"));
        Assert.That(service.GetSnapshot().Error, Is.Null);
    }

    private static AudioOutputCandidate Candidate(string name, Func<StubWavePlayer> create) =>
        new(name, () => new AudioOutputDevice(create));

    private static float[] Data() => Enumerable.Range(0, 88200).Select(i => (i % 997 - 498) / 1000f).ToArray();

    private static WaveStream Signal()
    {
        var data = Data();
        var bytes = new byte[data.Length * sizeof(float)];
        Buffer.BlockCopy(data, 0, bytes, 0, bytes.Length);
        return new RawSourceWaveStream(new MemoryStream(bytes), WaveFormat.CreateIeeeFloatWaveFormat(44100, 2));
    }
}
