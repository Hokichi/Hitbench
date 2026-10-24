using Onset.Core.Entities;
using Onset.Services;

namespace Onset.Test.Services;

public class ControlPointPlaybackTests
{
    [Test]
    public void DisabledBoundaryAndSeekPreserveSampleTailAndSongGain()
    {
        foreach (var start in new[] { 0, 44 })
        {
            var mixer = Mixer(start); mixer.SetControlPoints(new([new(0, 80), new(1, 20, true), new(2, 40)]));
            var output = new float[300 - start * 2]; mixer.Read(output, 0, output.Length);
            Assert.That(output[0], Is.EqualTo(.09f).Within(.0001f));
            Assert.That(output[176 - start * 2], Is.EqualTo(.07f).Within(.0001f));
        }
    }

    [Test]
    public async Task LiveServiceEditsPreserveOutputPreparationAndTransport()
    {
        using var project = new TestProjectFolder("hit.wav", "slow.wav");
        using var release = new ManualResetEventSlim();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var players = new List<StubWavePlayer>(); var decodes = 0;
        using var audio = new WorkstationAudioService(path =>
        {
            Interlocked.Increment(ref decodes);
            if (path.EndsWith("slow.wav")) { entered.TrySetResult(); if (!release.Wait(TimeSpan.FromSeconds(5))) throw new TimeoutException(); }
            return ScheduledPlaybackTests.Stream(path.EndsWith("song.wav") ? .2f : .4f);
        }, () => { var player = new StubWavePlayer(); players.Add(player); return player; });
        audio.Open("song.wav");
        await audio.UpdateSamplesAsync(project.ProjectPath, [new(1, "hit.wav", 0, 100)], default);
        audio.SetControlPoints([new(0,50)]); audio.Play();
        Assert.That(ScheduledPlaybackTests.Read(players[^1]), Is.All.EqualTo(.4f).Within(.0001f));
        var count = decodes; audio.SetControlPoints([new(0,25)]);
        Assert.That(decodes, Is.EqualTo(count)); Assert.That(players, Has.Count.EqualTo(1));
        Assert.That(audio.GetSnapshot().IsPlaying, Is.True);
        Assert.That(ScheduledPlaybackTests.Read(players[^1]), Is.All.EqualTo(.3f).Within(.0001f));
        var update = audio.UpdateSamplesAsync(project.ProjectPath, [new(2, "slow.wav", 0, 100)], default);
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5)); audio.SetControlPoints([new(0,0)]);
            release.Set(); await update;
            Assert.That(ScheduledPlaybackTests.Read(players[^1]), Is.All.EqualTo(.2f).Within(.0001f));
            audio.Seek(TimeSpan.FromMilliseconds(100));
            Assert.That(ScheduledPlaybackTests.Read(players[^1]), Is.All.EqualTo(.2f).Within(.0001f));
            audio.Stop(); audio.Play();
            Assert.That(ScheduledPlaybackTests.Read(players[^1]), Is.All.EqualTo(.2f).Within(.0001f));
            audio.Open("song.wav"); await audio.UpdateSamplesAsync(project.ProjectPath, [new(1, "hit.wav", 0, 100)], default); audio.Play();
            Assert.That(ScheduledPlaybackTests.Read(players[^1]), Is.All.EqualTo(.6f).Within(.0001f));
        }
        finally { release.Set(); await update; }
    }
    private static ScheduledSampleMixer Mixer(long start = 0)
    {
        var mixer = new ScheduledSampleMixer(DecodedAudio.CopyFrom(Enumerable.Repeat(.2f, 400).ToArray()), start);
        mixer.Publish(new PreparedPlaybackSchedule([new(1, DecodedAudio.CopyFrom(Enumerable.Repeat(.4f, 400).ToArray()), .5f)]));
        mixer.SetVolumes(.5f, .5f, .5f); return mixer;
    }
    [Test]
    public void BoundaryInsideBufferAffectsOnlySamplesAndRingingTails()
    {
        var mixer = Mixer(); mixer.SetControlPoints(new([new(1,50), new(2,100), new(3,0)]));
        var output = new float[300]; Assert.That(mixer.Read(output,0,output.Length), Is.EqualTo(300));
        Assert.That(output[86], Is.EqualTo(.1f).Within(.0001f));
        Assert.That(output[88], Is.EqualTo(.075f).Within(.0001f));
        Assert.That(output[176], Is.EqualTo(.1f).Within(.0001f));
        Assert.That(output[264], Is.EqualTo(.05f).Within(.0001f));
        Assert.That(output.Where((_,i)=>i%2==0), Is.EqualTo(output.Where((_,i)=>i%2==1)));
    }
    [Test]
    public void LiveEditAndSeekKeepStereoCoherent()
    {
        var mixer = Mixer(44); mixer.SetControlPoints(new([new(1,50)]));
        var first = new float[1]; mixer.Read(first,0,1);
        mixer.SetControlPoints(new([new(1,25)])); var next = new float[3]; mixer.Read(next,0,3);
        Assert.That(first[0], Is.EqualTo(.075f).Within(.0001f));
        Assert.That(next, Is.EqualTo(new[]{.075f,.0625f,.0625f}).Within(.0001f));
        mixer.SetControlPoints(new([new(0,0),new(1,25),new(4,0)]));
        var unchanged = new float[2]; mixer.Read(unchanged,0,2);
        Assert.That(unchanged, Is.All.EqualTo(.0625f).Within(.0001f));
    }
    [Test]
    public void SampleScheduleRefreshDoesNotEraseTimeline()
    {
        var mixer = Mixer(); mixer.SetControlPoints(new([new(0,0)]));
        mixer.Publish(new PreparedPlaybackSchedule([new(2,DecodedAudio.CopyFrom(Enumerable.Repeat(.4f,400).ToArray()),1)]));
        var output = new float[2]; mixer.Read(output,0,2);
        Assert.That(output, Is.All.EqualTo(.05f).Within(.0001f));
        mixer.SetControlPoints(ControlPointVolumeTimeline.Empty); mixer.Read(output,0,2);
        Assert.That(output, Is.All.EqualTo(.15f).Within(.0001f));
    }
}
