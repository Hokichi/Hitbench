using NAudio.Wave;

namespace osuHitsounder.Services;

public sealed class ScheduledSampleMixer : ISampleProvider
{
    private readonly DecodedAudio song;
    private readonly object sync = new();
    private readonly Dictionary<long, long> activations = [];
    private PreparedPlaybackSchedule schedule = PreparedPlaybackSchedule.Empty;
    private PlaybackGains gains = new(1, 1, 1);
    private long frame;
    private bool pending;
    private float right;
    private int faulted;

    public ScheduledSampleMixer(DecodedAudio song, long startFrame)
    {
        ArgumentNullException.ThrowIfNull(song);
        if (startFrame < 0) throw new ArgumentOutOfRangeException(nameof(startFrame));
        this.song = song;
        frame = Math.Min(startFrame, song.FrameCount);
    }

    public WaveFormat WaveFormat { get; } = WaveFormat.CreateIeeeFloatWaveFormat(44_100, 2);
    public long RenderedFrames => Volatile.Read(ref frame);
    public bool EndOfStream => RenderedFrames >= song.FrameCount;
    public bool Faulted => Volatile.Read(ref faulted) != 0;
    public void Publish(PreparedPlaybackSchedule value)
    {
        ArgumentNullException.ThrowIfNull(value);
        lock (sync)
        {
            schedule = value;
            var ids = value.Events.ToArray().Select(item => item.Id).ToHashSet();
            foreach (var id in activations.Keys.Where(id => !ids.Contains(id)).ToArray())
                activations.Remove(id);
        }
    }

    public void TriggerSamples(IReadOnlyList<long> placementIds)
    {
        ArgumentNullException.ThrowIfNull(placementIds);
        lock (sync)
        {
            var start = frame + (pending ? 1 : 0);
            foreach (var item in schedule.Events)
                if (placementIds.Contains(item.Id)) activations.TryAdd(item.Id, start);
        }
    }

    public SampleVoiceActivation[] CaptureVoices(long consumedFrame)
    {
        lock (sync)
        {
            var voices = new List<SampleVoiceActivation>();
            foreach (var item in schedule.Events)
                if (activations.TryGetValue(item.Id, out var start) && start + item.Audio.FrameCount > consumedFrame)
                    voices.Add(new SampleVoiceActivation(item.Id, start));
            return voices.ToArray();
        }
    }

    public void RestoreVoices(IReadOnlyList<SampleVoiceActivation> voices)
    {
        ArgumentNullException.ThrowIfNull(voices);
        lock (sync)
        {
            foreach (var voice in voices)
                foreach (var item in schedule.Events)
                    if (item.Id == voice.Id)
                    {
                        _ = checked(voice.StartFrame + item.Audio.FrameCount);
                        activations.TryAdd(voice.Id, voice.StartFrame);
                        break;
                    }
        }
    }

    public void SetVolumes(float song, float samples, float application) =>
        Volatile.Write(ref gains, new PlaybackGains(Normalize(song), Normalize(samples), Normalize(application)));

    public int Read(float[] buffer, int offset, int count)
    {
        lock (sync) return ReadCore(buffer, offset, count);
    }

    private int ReadCore(float[] buffer, int offset, int count)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        if (offset < 0 || count < 0 || offset > buffer.Length - count) throw new ArgumentOutOfRangeException(nameof(offset));
        var destination = buffer.AsSpan(offset, count);
        if (Faulted) { destination.Clear(); return count; }
        var current = schedule;
        var volume = Volatile.Read(ref gains);
        try
        {
            var written = 0;
            if (pending && count > 0)
            {
                destination[written++] = right;
                pending = false;
                Volatile.Write(ref frame, frame + 1);
            }
            var frames = (int)Math.Min((count - written) / 2, song.FrameCount - frame);
            Render(destination.Slice(written, frames * 2), frame, current, volume);
            written += frames * 2;
            Volatile.Write(ref frame, frame + frames);
            if (written < count && frame < song.FrameCount)
            {
                Span<float> pair = stackalloc float[2];
                Render(pair, frame, current, volume);
                destination[written++] = pair[0];
                right = pair[1];
                pending = true;
            }
            return written;
        }
        catch
        {
            destination.Clear();
            Volatile.Write(ref faulted, 1);
            return count;
        }
    }

    private void Render(Span<float> output, long start, PreparedPlaybackSchedule current, PlaybackGains volume)
    {
        var source = song.Samples.Slice(checked((int)(start * 2)), output.Length);
        for (var i = 0; i < output.Length; i++) output[i] = Finite(source[i]) * volume.Song;
        var end = start + output.Length / 2;
        foreach (var item in current.Events)
        {
            if (!activations.TryGetValue(item.Id, out var voiceStart)) continue;
            var voiceEnd = checked(voiceStart + item.Audio.FrameCount);
            if (voiceStart >= end || voiceEnd <= start) continue;
            var overlap = Math.Max(start, voiceStart);
            var length = checked((int)((Math.Min(end, voiceEnd) - overlap) * 2));
            var target = checked((int)((overlap - start) * 2));
            var input = item.Audio.Samples.Slice(checked((int)((overlap - voiceStart) * 2)), length);
            var gain = item.Gain * volume.Samples;
            for (var i = 0; i < length; i++) output[target + i] += Finite(input[i]) * gain;
        }
        for (var i = 0; i < output.Length; i++) output[i] = Math.Clamp(Finite(output[i] * volume.Application), -1, 1);
    }

    private static float Finite(float value) => float.IsFinite(value) ? value : 0;
    private static float Normalize(float value) => Math.Clamp(Finite(value), 0, 1);
}
