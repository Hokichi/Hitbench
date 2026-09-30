using osuHitsounder.Services;

namespace osuHitsounder.Test.Services;

public class AudioOutputDeviceTests
{
    [TestCase(48000, 16)]
    [TestCase(96000, 32)]
    public void PositionUsesOutputByteRateAndCannotExceedProducedFrames(int rate, int bits)
    {
        var player = new StubWavePlayer
        {
            Position = 800,
            ConfiguredOutputFormat = new NAudio.Wave.WaveFormat(rate, bits, 2)
        };
        using var output = new AudioOutputDevice(() => player);
        output.Init(new TestSampleProvider([0, 0], 44100, 2), () => 88200);
        output.Play();
        player.Position = 800 + player.OutputWaveFormat.AverageBytesPerSecond;
        Assert.That(output.GetPlayedFrames(), Is.EqualTo(44100));
        player.Position = 0;
        Assert.That(output.GetPlayedFrames(), Is.EqualTo(44100));
        player.Position = long.MaxValue;
        Assert.That(output.GetPlayedFrames(), Is.EqualTo(88200));
    }

    [Test]
    public void BackendCompletionWaitsForQueuedTail()
    {
        var player = new StubWavePlayer();
        using var output = new AudioOutputDevice(() => player);
        output.Init(new TestSampleProvider([.2f, .3f], 44100, 2), () => 1, () => true);
        var completions = 0;
        output.PlaybackStopped += (_, _) => completions++;
        output.Play();
        player.Position = 8000;
        output.Poll();
        Assert.That(output.PlaybackState, Is.EqualTo(NAudio.Wave.PlaybackState.Playing));
        Assert.That(completions, Is.Zero);
        player.RaiseStopped();
        output.Poll();
        output.Poll();
        Assert.That(completions, Is.EqualTo(1));
        Assert.That(output.PlaybackState, Is.EqualTo(NAudio.Wave.PlaybackState.Stopped));
    }

    [Test]
    public void ProviderFailureSilencesHardwareReadsAndDefersErrorUntilPoll()
    {
        var source = new TestSampleProvider([.5f,.5f], 44100, 2) { FailRead = true };
        var player = new StubWavePlayer();
        using var output = new AudioOutputDevice(() => player);
        output.Init(source);
        output.Play();
        Exception? failure = null;
        output.PlaybackStopped += (_, args) => failure = args.Exception;
        var bytes = Enumerable.Repeat((byte)255, 8).ToArray();
        Assert.That(player.Provider!.Read(bytes, 0, bytes.Length), Is.EqualTo(8));
        Assert.That(bytes, Is.All.Zero);
        Assert.That(failure, Is.Null);
        Assert.That(output.PlaybackState, Is.EqualTo(NAudio.Wave.PlaybackState.Playing));
        source.FailRead = false;
        player.Provider.Read(bytes, 0, bytes.Length);
        Assert.That(bytes, Is.All.Zero);
        output.Poll();
        Assert.That(failure?.Message, Does.Contain("rendering failed"));
        Assert.That(output.PlaybackState, Is.EqualTo(NAudio.Wave.PlaybackState.Stopped));
    }

    [Test]
    public void FailedPositionReadBecomesStoppedErrorInsteadOfEscapingToUi()
    {
        var player = new StubWavePlayer();
        using var output = new AudioOutputDevice(()=>player);
        output.Init(new ScheduledSampleMixer(DecodedAudio.CopyFrom(new float[20]),0));
        output.Play();
        player.FailPosition = true;
        Exception? failure = null;
        output.PlaybackStopped += (_,args)=>failure=args.Exception;
        Assert.DoesNotThrow(()=>output.GetPlayedFrames());
        output.Poll();
        Assert.That(failure, Is.TypeOf<IOException>());
        Assert.That(output.PlaybackState, Is.EqualTo(NAudio.Wave.PlaybackState.Stopped));
    }

    [Test]
    public void EstimatedPositionDrainsFinalBufferAndResetIsDeferred()
    {
        long now = 0, produced = 1000;
        var ended = false;
        var reset = false;
        var player = new StubWavePlayer();
        using var output = new AudioOutputDevice(() => player, getLatencyFrames: () => 100,
            resetRequested: () => reset, getTimestamp: () => now, timestampFrequency: 44100,
            completeFromRenderedFrames: true);
        output.Init(new ScheduledSampleMixer(DecodedAudio.CopyFrom(new float[2000]),0),()=>produced,()=>ended);
        output.Play();
        Assert.That(output.GetPlayedFrames(), Is.EqualTo(900));
        ended = true;
        output.Poll();
        Assert.That(output.PlaybackState, Is.EqualTo(NAudio.Wave.PlaybackState.Playing));
        now = 100;
        output.Poll();
        Assert.That(output.GetPlayedFrames(), Is.EqualTo(1000));
        Assert.That(output.PlaybackState, Is.EqualTo(NAudio.Wave.PlaybackState.Stopped));
        Assert.That(player.IsDisposed, Is.False);
    }

    [Test]
    public void ResetAndRendererFaultStopOnlyDuringControlPoll()
    {
        var reset = false;
        var player = new StubWavePlayer();
        using var output = new AudioOutputDevice(()=>player, resetRequested:()=>reset);
        output.Init(new ScheduledSampleMixer(DecodedAudio.CopyFrom(new float[20]),0));
        Exception? error = null;
        output.PlaybackStopped += (_,args)=>error=args.Exception;
        output.Play();
        reset = true;
        Assert.That(output.PlaybackState, Is.EqualTo(NAudio.Wave.PlaybackState.Playing));
        output.Poll();
        Assert.That(error, Is.Not.Null);
        Assert.That(output.PlaybackState, Is.EqualTo(NAudio.Wave.PlaybackState.Stopped));
    }

    [Test]
    public void PlayedCursorExcludesOriginAndNeverMovesBackward()
    {
        var player = new StubWavePlayer { Position = 800 };
        using var output = new AudioOutputDevice(() => player);
        output.Init(new ScheduledSampleMixer(DecodedAudio.CopyFrom(new float[20000]),0));
        output.Play();
        player.Position = 800 + 1024 * 8;
        Assert.That(output.GetPlayedFrames(), Is.EqualTo(1024));
        player.Position = 0;
        Assert.That(output.GetPlayedFrames(), Is.EqualTo(1024));
    }

    [Test]
    public void DeviceCallbacksWaitForControlPollAndRetirementDetachesThem()
    {
        var player = new StubWavePlayer();
        using var output = new AudioOutputDevice(() => player);
        output.Init(new ScheduledSampleMixer(DecodedAudio.CopyFrom(new float[20]),0));
        var notifications = 0;
        output.PlaybackStopped += (_,_) => notifications++;
        output.Play();
        player.RaiseStopped(new InvalidOperationException("lost"));
        Assert.That(notifications, Is.Zero);
        output.Poll();
        Assert.That(notifications, Is.EqualTo(1));
        output.Dispose();
        player.RaiseRetiredStopped();
        output.Poll();
        Assert.That(notifications, Is.EqualTo(1));
        Assert.That(player.IsDisposed, Is.True);
    }

    [Test]
    public void FailedInitializationDisposesPartialPlayer()
    {
        var player = new StubWavePlayer { FailInitialization = true };
        using var output = new AudioOutputDevice(() => player);
        Assert.Throws<InvalidOperationException>(() => output.Init(new ScheduledSampleMixer(DecodedAudio.CopyFrom([0,0]),0)));
        Assert.That(player.IsDisposed, Is.True);
        Assert.That(player.HasPlaybackStoppedSubscribers, Is.False);
    }
}
