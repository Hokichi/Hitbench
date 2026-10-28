using Hitbench.Services;
using NAudio.Wave;

namespace Hitbench.Test.Services;

public class WorkstationAudioBudgetTests
{
    [Test]
    public async Task ClosingDuringDecodeStopsBeforeRetainingFurtherChunks()
    {
        var budget = new AudioMemoryBudget(1000000, 1000000);
        long firstReadCharge = 0;
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        using var project = new TestProjectFolder();
        using var audio = new WorkstationAudioService(_ => new ShortReadWaveStream(new byte[8192],
            WaveFormat.CreateIeeeFloatWaveFormat(44100, 2), () => { firstReadCharge = budget.UsedBytes; entered.Set(); release.Wait(TimeSpan.FromSeconds(5)); }),
            [new("Test", () => new StubAudioOutput())], memoryBudget: budget);
        audio.Open("song.wav");
        var pending = audio.UpdateSamplesAsync(project.ProjectPath, [], default);
        try
        {
            Assert.That(await Task.Run(() => entered.Wait(TimeSpan.FromSeconds(3))), Is.True);
            audio.Dispose();
        }
        finally { release.Set(); }
        await pending;
        Assert.That(budget.UsedBytes, Is.Zero);
        Assert.That(budget.PeakBytes, Is.EqualTo(firstReadCharge));
    }

    [Test]
    public async Task EquivalentPathsSharePcmWithoutDroppingEitherVoice()
    {
        var budget = new AudioMemoryBudget(200000, 200000);
        using var project = new TestProjectFolder();
        var kit = Directory.CreateDirectory(Path.Combine(Path.GetDirectoryName(project.ProjectPath)!, "Samples", "kit"));
        File.WriteAllBytes(Path.Combine(kit.FullName, "a.wav"), [0]);
        var player = new StubAudioOutput();
        using var audio = new WorkstationAudioService(path =>
        {
            var values = Enumerable.Repeat(path.EndsWith("song.wav") ? 0f : .25f, 64).ToArray();
            var bytes = new byte[256]; Buffer.BlockCopy(values, 0, bytes, 0, bytes.Length);
            return new RawSourceWaveStream(new MemoryStream(bytes), WaveFormat.CreateIeeeFloatWaveFormat(44100, 2));
        }, [new("Test", () => player)], memoryBudget: budget);
        audio.Open("song.wav");
        await audio.UpdateSamplesAsync(project.ProjectPath, [new(1, "kit/a.wav", 0, 100), new(2, "kit\\a.wav", 0, 100)], default);
        audio.Play();
        var pcm = new float[2]; player.Source!.Read(pcm, 0, 2);
        Assert.That(audio.GetSnapshot().Error, Is.Null);
        Assert.That(pcm, Is.EqualTo(new[] { .5f, .5f }));
    }

    [Test]
    public async Task RejectedReplacementKeepsPreparedPlaybackUsableAndReleasesAllOwners()
    {
        var budget = new AudioMemoryBudget(200000, 200000);
        using var project = new TestProjectFolder("old.wav", "large.wav");
        var player = new StubAudioOutput();
        WaveStream Open(string path)
        {
            var samples = Enumerable.Repeat(path.EndsWith("old.wav") ? .25f : .125f, path.EndsWith("large.wav") ? 32768 : 64).ToArray();
            var data = new byte[samples.Length * 4]; Buffer.BlockCopy(samples, 0, data, 0, data.Length);
            return new RawSourceWaveStream(new MemoryStream(data), WaveFormat.CreateIeeeFloatWaveFormat(44100, 2));
        }
        using (var audio = new WorkstationAudioService(Open, [new("Test", () => player)], memoryBudget: budget))
        {
            audio.Open("song.wav");
            await audio.UpdateSamplesAsync(project.ProjectPath, [new(1, "old.wav", 0, 100)], default);
            audio.Play();
            await audio.UpdateSamplesAsync(project.ProjectPath, [new(2, "large.wav", 0, 100)], default, true);
            Assert.That(audio.IsPrepared, Is.True);
            Assert.That(audio.GetSnapshot().Error, Does.Contain("memory"));
            var pcm = new float[2]; player.Source!.Read(pcm, 0, 2);
            Assert.That(pcm, Is.EqualTo(new[] { .375f, .375f }));
            Assert.That(budget.PeakBytes, Is.LessThanOrEqualTo(200000));
        }
        Assert.That(budget.UsedBytes, Is.Zero);
    }

    [Test]
    public void MixerOwnsItsHandlesEvenAfterCacheOwnerDisposal()
    {
        var budget = new AudioMemoryBudget(10000, 10000);
        var song = DecodedAudio.CopyFrom(new float[8], budget);
        var sample = DecodedAudio.CopyFrom(new[] { .5f, .5f }, budget);
        using (var mixer = new ScheduledSampleMixer(song, 0))
        {
            mixer.Publish(new PreparedPlaybackSchedule([new(1, sample, 1), new(2, sample, 1)]));
            song.Dispose(); sample.Dispose();
            Assert.That(budget.UsedBytes, Is.EqualTo(40));
            var output = new float[2]; mixer.Read(output, 0, 2);
            Assert.That(output, Is.EqualTo(new[] { 1f, 1f }));
        }
        Assert.That(budget.UsedBytes, Is.Zero);
    }
}
