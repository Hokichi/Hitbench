using NAudio.Wave;
using Onset.Core.Entities;
using Onset.Services;
using Onset.Test.Presentation;
using Onset.ViewModels.Shell;

namespace Onset.Test.Services;

public class MarkerAudioIntegrationTests
{
    [Test]
    public async Task AudioOnsetPrecedesBufferedMarkerCrossingAndSeekResetsPosition()
    {
        using var project = new TestProjectFolder("hit.wav");
        var players = new List<StubWavePlayer>();
        using var audio = new WorkstationAudioService(path => Signal(path.EndsWith("hit.wav") ? .4f : .2f),
            () => { var player = new StubWavePlayer(); players.Add(player); return player; });
        var source = new StubWorkstationSourceService
        {
            Load = (_, _) => Task.FromResult(new WorkstationSource("map", "song.wav", new ProjectData
            {
                Offsets = [0], BPMs = [120], Tracks = [new Track
                {
                    Samples = [new Sample { FileName = "hit.wav", Timestamp = 10 }]
                }]
            }))
        };
        var vm = new WorkstationPageVM(source, new Onset.Services.MarkerPlaybackCoordinator(audio, (duration, token) => Task.Delay(duration, token)), new ImmediateUiScheduler());
        try
        {
            await vm.LoadAsync(project.ProjectPath, CancellationToken.None);
            vm.AcknowledgePresentationReady(vm.LoadGeneration);
            await vm.PlayPauseCommand.ExecuteAsync(null);
            var output = new float[1200];
            players[^1].Provider!.ToSampleProvider().Read(output, 0, output.Length);
            Assert.That(output[..882], Is.All.EqualTo(.2f));
            Assert.That(output[882..], Is.EqualTo(Enumerable.Repeat(.6f, 318).ToArray()).Within(.0001));
            players[^1].Position = 600 * 8;
            await StubWorkstationAudioService.WaitUntilAsync(() => { vm.RefreshPlayback(); return vm.Position.TotalMilliseconds >= 10; });
            vm.RefreshPlayback();
            Assert.That(ReadFrame(players[^1]), Is.EqualTo(new[] { .6f, .6f }).Within(.0001));
            vm.RefreshPlayback();
            await vm.SeekCommand.ExecuteAsync(TimeSpan.Zero);
            Assert.That(ReadFrame(players[^1]), Is.EqualTo(new[] { .2f,.2f }).Within(.0001));
        }
        finally { await vm.DeactivateAsync(); }
    }

    [Test]
    public async Task PreparationReplacedBeforeCompletionCannotRestoreOldPlacements()
    {
        using var project = new TestProjectFolder("hit.wav");
        using var release = new ManualResetEventSlim();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var player = new StubWavePlayer();
        using var audio = new WorkstationAudioService(path =>
        {
            if (path.EndsWith("hit.wav"))
            {
                entered.TrySetResult();
                if (!release.Wait(TimeSpan.FromSeconds(5))) throw new TimeoutException("Test decode release missing");
                return Signal(.4f);
            }
            return Signal(.2f);
        }, () => player);
        audio.Open("song.wav");
        var first = audio.UpdateSamplesAsync(project.ProjectPath, [new SamplePlaybackEvent(1, "hit.wav", 0, 100)], CancellationToken.None);
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var replacement = audio.UpdateSamplesAsync(project.ProjectPath, [new SamplePlaybackEvent(2, "hit.wav", 0, 100)], CancellationToken.None);
            release.Set();
            await Task.WhenAll(first, replacement);
            audio.Play();

            Assert.That(ReadFrame(player), Is.EqualTo(new[] { .6f, .6f }).Within(.0001));
        }
        finally { release.Set(); await first; }
    }

    [Test]
    public async Task NewScheduleRendersOverlappingPastTail()
    {
        using var project = new TestProjectFolder("hit.wav");
        var player = new StubWavePlayer();
        using var audio = new WorkstationAudioService(path => Signal(path.EndsWith("hit.wav") ? .4f : .2f), () => player);
        audio.Open("song.wav");
        await audio.UpdateSamplesAsync(project.ProjectPath, [], CancellationToken.None);
        audio.Play();
        await audio.UpdateSamplesAsync(project.ProjectPath, [new SamplePlaybackEvent(1, "hit.wav", 0, 100)], CancellationToken.None);
        Assert.That(ReadFrame(player), Is.EqualTo(new[] { .6f, .6f }).Within(.0001));
        Assert.That(ReadFrame(player), Is.EqualTo(new[] { .6f, .6f }).Within(.0001));
    }

    private static WaveStream Signal(float value)
    {
        var samples = Enumerable.Repeat(value, 44100 * 2).ToArray();
        var bytes = new byte[samples.Length * sizeof(float)];
        Buffer.BlockCopy(samples, 0, bytes, 0, bytes.Length);
        return new RawSourceWaveStream(new MemoryStream(bytes), WaveFormat.CreateIeeeFloatWaveFormat(44100, 2));
    }

    private static float[] ReadFrame(StubWavePlayer player)
    {
        var frame = new float[2];
        Assert.That(player.Provider!.ToSampleProvider().Read(frame, 0, 2), Is.EqualTo(2));
        return frame;
    }
}
