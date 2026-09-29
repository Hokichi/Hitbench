using osuHitsounder.Services;
using osuHitsounder.Core.Entities;

namespace osuHitsounder.Test.Services;

public class AudioOutputFactoryTests
{
    [TestCase(null)]
    [TestCase("")]
    [TestCase("  ")]
    [TestCase("My ASIO")]
    public void FactorySelectsRequestedBackendWithoutOpeningUntilInit(string? name)
    {
        string? selected = null;
        var factory = new AudioOutputFactory(driver => { selected=driver; return new StubWavePlayer(); },
            () => { selected="wave"; return new StubWavePlayer(); });
        using var output = factory.Create(name);
        Assert.That(selected, Is.Null);
        output.Init(new ScheduledSampleMixer(DecodedAudio.CopyFrom([0,0]),0));
        Assert.That(selected, Is.EqualTo(string.IsNullOrWhiteSpace(name)?"wave":name));
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
        if (failFallback) Assert.That(snapshot.Error, Does.Contain("WaveOut"));
        else
        {
            Assert.That(snapshot.Error, Is.Null);
            Assert.That(snapshot.Warning, Does.Contain("WaveOut"));
            Assert.That(ScheduledPlaybackTests.Read(fallback),Is.EqualTo(new[]{.2f,.2f}));
        }
    }
}
