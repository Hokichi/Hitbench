using NAudio.Wave;

namespace osuHitsounder.Services;

public sealed class ScheduledSampleMixer : ISampleProvider
{
    private readonly object sync = new();
    private readonly ReadOnlyMemory<float> song;
    private readonly Dictionary<long, (PreparedSampleEvent Event, int Position)> active = [];
    private PreparedSampleEvent[] schedule = [];
    private long samplePosition;
    private int nextEvent;
    private float songVolume = 1;
    private float samplesVolume = 1;
    private float applicationVolume = 1;

    public ScheduledSampleMixer(ReadOnlyMemory<float> song, WaveFormat format, long startFrame)
    {
        if (format.Encoding != WaveFormatEncoding.IeeeFloat)
            throw new ArgumentException("Mixer format must use IEEE float samples.", nameof(format));
        if (startFrame < 0) throw new ArgumentOutOfRangeException(nameof(startFrame));
        this.song = song;
        WaveFormat = format;
        samplePosition = Math.Min(song.Length, checked(startFrame * format.Channels));
    }

    public WaveFormat WaveFormat { get; }

    public float SongVolume
    {
        get { lock (sync) return songVolume; }
        set { lock (sync) songVolume = Math.Clamp(float.IsFinite(value) ? value : 0, 0, 1); }
    }

    public float SamplesVolume
    {
        get { lock (sync) return samplesVolume; }
        set { lock (sync) samplesVolume = NormalizeVolume(value); }
    }

    public float ApplicationVolume
    {
        get { lock (sync) return applicationVolume; }
        set { lock (sync) applicationVolume = NormalizeVolume(value); }
    }

    public void ReplaceSchedule(IReadOnlyList<PreparedSampleEvent> events)
    {
        ArgumentNullException.ThrowIfNull(events);
        lock (sync)
        {
            var replacement = events.OrderBy(item => item.StartFrame).ThenBy(item => item.Id).ToArray();
            var byId = replacement.ToDictionary(item => item.Id);
            foreach (var id in active.Keys.ToArray())
            {
                if (!byId.TryGetValue(id, out var updated)) active.Remove(id);
                else active[id] = (updated, active[id].Position);
            }
            schedule = replacement;
            var currentFrame = samplePosition / WaveFormat.Channels;
            nextEvent = Array.FindIndex(schedule, item => item.StartFrame >= currentFrame);
            if (nextEvent < 0) nextEvent = schedule.Length;
        }
    }

    public int Read(float[] buffer, int offset, int count)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        if (offset < 0 || count < 0 || offset > buffer.Length - count)
            throw new ArgumentOutOfRangeException(nameof(offset));

        lock (sync)
        {
            var available = (int)Math.Min(count, song.Length - samplePosition);
            if (available <= 0) return 0;
            var channels = WaveFormat.Channels;
            for (var outputIndex = 0; outputIndex < available; outputIndex++)
            {
                var channel = (int)(samplePosition % channels);
                var frame = samplePosition / channels;
                if (channel == 0) ActivateEvents(frame);

                var mixed = FiniteOrZero(song.Span[(int)samplePosition]) * songVolume;
                foreach (var id in active.Keys.ToArray())
                {
                    var voice = active[id];
                    if (voice.Position >= voice.Event.Samples.Length)
                    {
                        active.Remove(id);
                        continue;
                    }
                    mixed += FiniteOrZero(voice.Event.Samples.Span[voice.Position]) *
                        Math.Clamp(FiniteOrZero(voice.Event.Gain), 0, 1) * samplesVolume;
                    voice.Position++;
                    if (voice.Position >= voice.Event.Samples.Length) active.Remove(id);
                    else active[id] = voice;
                }
                buffer[offset + outputIndex] = Math.Clamp(FiniteOrZero(mixed) * applicationVolume, -1, 1);
                samplePosition++;
            }
            return available;
        }
    }

    private void ActivateEvents(long frame)
    {
        while (nextEvent < schedule.Length && schedule[nextEvent].StartFrame <= frame)
        {
            var item = schedule[nextEvent++];
            if (item.StartFrame == frame && item.Samples.Length > 0 && !active.ContainsKey(item.Id))
                active.Add(item.Id, (item, 0));
        }
    }

    private static float FiniteOrZero(float value) => float.IsFinite(value) ? value : 0;
    private static float NormalizeVolume(float value) => Math.Clamp(FiniteOrZero(value), 0, 1);
}
