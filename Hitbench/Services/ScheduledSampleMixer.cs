using NAudio.Wave;

namespace Hitbench.Services;

public sealed class ScheduledSampleMixer : ISampleProvider, IDisposable
{
    private readonly DecodedAudio song;
    private readonly object sync = new();
    private PreparedPlaybackSchedule schedule = PreparedPlaybackSchedule.Empty;
    private ControlPointVolumeTimeline controlPoints = ControlPointVolumeTimeline.Empty;
    private PlaybackGains gains = new(1, 1, 1);
    private long frame;
    private bool pending;
    private float right;
    private int faulted;
    private volatile bool disposed;
    private Dictionary<AudioPcmBuffer, DecodedAudio> ownedSamples = new();

    public ScheduledSampleMixer(DecodedAudio song, long startFrame)
    {
        ArgumentNullException.ThrowIfNull(song);
        if (startFrame < 0) throw new ArgumentOutOfRangeException(nameof(startFrame));
        this.song = song.Retain();
        frame = Math.Min(startFrame, song.FrameCount);
    }

    public WaveFormat WaveFormat { get; } = WaveFormat.CreateIeeeFloatWaveFormat(44_100, 2);
    public long RenderedFrames => Volatile.Read(ref frame);
    public bool EndOfStream => disposed || RenderedFrames >= song.FrameCount;
    public bool Faulted => Volatile.Read(ref faulted) != 0;
    public void Publish(PreparedPlaybackSchedule value)
    {
        ArgumentNullException.ThrowIfNull(value);
        lock (sync)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            var next = new Dictionary<AudioPcmBuffer, DecodedAudio>();
            try
            {
                var events = new List<PreparedSampleEvent>(value.Events.Length);
                foreach (var item in value.Events)
                {
                    if (!next.TryGetValue(item.Audio.Storage, out var handle))
                        next.Add(item.Audio.Storage, handle = item.Audio.Retain());
                    events.Add(item with { Audio = handle });
                }
                var snapshot = new PreparedPlaybackSchedule(events);
                var retired = ownedSamples; ownedSamples = next; schedule = snapshot;
                foreach (var handle in retired.Values) handle.Dispose();
            }
            catch { foreach (var handle in next.Values) handle.Dispose(); throw; }
        }
    }

    public void SetVolumes(float song, float samples, float application) =>
        Volatile.Write(ref gains, new PlaybackGains(Normalize(song), Normalize(samples), Normalize(application)));

    public void SetControlPoints(ControlPointVolumeTimeline timeline)
    {
        ArgumentNullException.ThrowIfNull(timeline);
        lock (sync) controlPoints = timeline;
    }

    public int Read(float[] buffer, int offset, int count)
    {
        lock (sync) return disposed ? 0 : ReadCore(buffer, offset, count);
    }

    public void Dispose()
    {
        lock (sync)
        {
            if (disposed) return;
            disposed = true;
            schedule = PreparedPlaybackSchedule.Empty;
            foreach (var handle in ownedSamples.Values) handle.Dispose();
            ownedSamples.Clear(); song.Dispose();
        }
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
        var range = current.GetOverlappingRange(start, end);
        foreach (var item in current.Events[range.Start..range.End])
        {
            if (item.StartFrame >= end) break;
            if (item.EndFrame <= start) continue;
            var overlap = Math.Max(start, item.StartFrame);
            var overlapEnd = Math.Min(end, item.EndFrame);
            while (overlap < overlapEnd)
            {
                var regionEnd = Math.Min(overlapEnd, controlPoints.GetNextBoundary(overlap));
                var length = checked((int)((regionEnd - overlap) * 2));
                var target = checked((int)((overlap - start) * 2));
                var input = item.Audio.Samples.Slice(checked((int)((overlap - item.StartFrame) * 2)), length);
                var gain = item.Gain * volume.Samples * controlPoints.GetGain(overlap);
                for (var i = 0; i < length; i++) output[target + i] += Finite(input[i]) * gain;
                overlap = regionEnd;
            }
        }
        for (var i = 0; i < output.Length; i++) output[i] = Math.Clamp(Finite(output[i] * volume.Application), -1, 1);
    }

    private static float Finite(float value) => float.IsFinite(value) ? value : 0;
    private static float Normalize(float value) => Math.Clamp(Finite(value), 0, 1);
}

