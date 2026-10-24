using Onset.Services;
using Onset.Core.Entities;

namespace Onset.Test.Services;

public class AudioOutputFactoryTests
{
    [TestCase(null)]
    [TestCase("")]
    [TestCase("  ")]
    [TestCase("Studio ASIO")]
    public void CandidatesAreOrderedAndDeferred(string? driver)
    {
        var opened = new List<string>();
        var factory = new AudioOutputFactory(
            name => { opened.Add(name); return new StubWavePlayer(); },
            () => { opened.Add("WASAPI"); return new StubWavePlayer(); },
            () => { opened.Add("WaveOut"); return new StubWavePlayer(); });
        var candidates = factory.CreateCandidates(driver);
        var hasAsio = !string.IsNullOrWhiteSpace(driver);
        Assert.That(candidates.Select(x => x.Name), Is.EqualTo(hasAsio
            ? new[] { "ASIO", "WASAPI", "WaveOut" } : new[] { "WASAPI", "WaveOut" }));
        Assert.That(opened, Is.Empty);
        foreach (var candidate in candidates)
        {
            using var output = candidate.Create();
            output.Init(new ScheduledSampleMixer(DecodedAudio.CopyFrom([0, 0]), 0));
        }
        Assert.That(opened, Is.EqualTo(hasAsio
            ? new[] { driver!, "WASAPI", "WaveOut" } : new[] { "WASAPI", "WaveOut" }));
    }

    [Test]
    public async Task InitializationCleanupNeverHoldsServiceLock()
    {
        using var project = new TestProjectFolder();
        var player = new StubWavePlayer { FailInitialization=true };
        using var service = new WorkstationAudioService(_=>ScheduledPlaybackTests.Stream(.2f),()=>player);
        service.Open("song.wav");
        await service.UpdateSamplesAsync(project.ProjectPath,[],CancellationToken.None);
        var lockWasAvailable = false;
        player.Disposing=()=> lockWasAvailable=Task.Run(service.GetSnapshot).Wait(TimeSpan.FromSeconds(1));
        service.Play();
        Assert.That(lockWasAvailable, Is.True);
    }

    [Test]
    public async Task ResetWaitsForExplicitPlayThenUsesFreshFallbackSession()
    {
        using var project = new TestProjectFolder();
        var preferred = new StubWavePlayer();
        var fallback = new StubWavePlayer();
        var reset = false;
        var fallbacks = 0;
        using var service = new WorkstationAudioService(_=>ScheduledPlaybackTests.Stream(.2f),
            ()=>new AudioOutputDevice(()=>preferred,resetRequested:()=>reset),
            ()=>{fallbacks++;return new AudioOutputDevice(()=>fallback);});
        service.Open("song.wav");
        await service.UpdateSamplesAsync(project.ProjectPath,[],CancellationToken.None);
        service.Play();
        reset=true;
        Assert.That(service.GetSnapshot().IsPlaying,Is.False);
        Assert.That(fallbacks,Is.Zero);
        service.Play();
        Assert.That(service.GetSnapshot().IsPlaying,Is.True);
        Assert.That(fallbacks,Is.EqualTo(1));
        Assert.That(ScheduledPlaybackTests.Read(fallback),Is.EqualTo(new[]{.2f,.2f}));
    }

    [Test]
    public async Task StartFailureFallsBackWithoutLosingSongOrigin()
    {
        using var project = new TestProjectFolder();
        var preferred = new StubWavePlayer {FailPlayback=true};
        var fallback = new StubWavePlayer();
        using var service = new WorkstationAudioService(_=>ScheduledPlaybackTests.Stream(.2f),
            ()=>new AudioOutputDevice(()=>preferred),()=>new AudioOutputDevice(()=>fallback));
        service.Open("song.wav");
        await service.UpdateSamplesAsync(project.ProjectPath,[],CancellationToken.None);
        service.Seek(TimeSpan.FromMilliseconds(250));
        service.Play();
        Assert.That(service.GetSnapshot().Position,Is.EqualTo(TimeSpan.FromMilliseconds(250)));
        Assert.That(service.GetSnapshot().IsPlaying,Is.True);
        Assert.That(preferred.IsDisposed,Is.True);
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("  ")]
    [TestCase("My ASIO")]
    public void FactorySelectsRequestedBackendWithoutOpeningUntilInit(string? name)
    {
        string? selected = null;
        var factory = new AudioOutputFactory(driver => { selected=driver; return new StubWavePlayer(); },
            () => { selected="wasapi"; return new StubWavePlayer(); },
            () => { selected="wave"; return new StubWavePlayer(); });
        using var output = factory.CreateCandidates(name)[0].Create();
        Assert.That(selected, Is.Null);
        output.Init(new ScheduledSampleMixer(DecodedAudio.CopyFrom([0,0]),0));
        Assert.That(selected, Is.EqualTo(string.IsNullOrWhiteSpace(name)?"wasapi":name));
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task FailedPreferredOutputFallsBackWithWarningOrReportsBothFailures(bool failFallback)
    {
        using var project = new TestProjectFolder("hit.wav");
        var preferred = new StubWavePlayer { FailInitialization=true };
        var fallback = new StubWavePlayer { FailInitialization=failFallback };
        using var service = new WorkstationAudioService(_=>ScheduledPlaybackTests.Stream(.2f),
            ()=>new AudioOutputDevice(()=>preferred),()=>new AudioOutputDevice(()=>fallback));
        service.Open("song.wav");
        await service.UpdateSamplesAsync(project.ProjectPath,[],CancellationToken.None);
        Assert.DoesNotThrow(service.Play);
        var snapshot = service.GetSnapshot();
        Assert.That(preferred.IsDisposed, Is.True);
        Assert.That(snapshot.IsPlaying, Is.EqualTo(!failFallback));
        if (failFallback) Assert.That(snapshot.Error, Does.Contain("Fallback"));
        else
        {
            Assert.That(snapshot.Error, Is.Null);
            Assert.That(snapshot.Warning, Does.Contain("Fallback"));
            Assert.That(ScheduledPlaybackTests.Read(fallback),Is.EqualTo(new[]{.2f,.2f}));
        }
    }
}
