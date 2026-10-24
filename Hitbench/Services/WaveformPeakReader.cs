using NAudio.Wave;
using Hitbench.Core.Entities;

namespace Hitbench.Services;

public sealed class WaveformPeakReader(int framesPerPeak = 256, int maximumPeaks = 65_536)
{
    public WaveformData Read(ISampleProvider source, CancellationToken token)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(framesPerPeak);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumPeaks);
        var channels = source.WaveFormat.Channels;
        var sampleRate = source.WaveFormat.SampleRate;
        var buffer = new float[Math.Max(channels * 1024, channels)];
        var peaks = new List<float>();
        var framesInPeak = 0;
        var currentPeak = 0f;
        long frameCount = 0;
        var currentFramesPerPeak = framesPerPeak;

        while (true)
        {
            token.ThrowIfCancellationRequested();
            var read = source.Read(buffer, 0, buffer.Length);
            if (read == 0) break;
            var completeSamples = read - read % channels;
            for (var sampleIndex = 0; sampleIndex < completeSamples; sampleIndex += channels)
            {
                var framePeak = 0f;
                for (var channel = 0; channel < channels; channel++)
                {
                    var sample = buffer[sampleIndex + channel];
                    if (float.IsFinite(sample)) framePeak = Math.Max(framePeak, Math.Min(1f, Math.Abs(sample)));
                }
                currentPeak = Math.Max(currentPeak, framePeak);
                framesInPeak++;
                frameCount++;
                if (framesInPeak < currentFramesPerPeak) continue;
                peaks.Add(currentPeak);
                currentPeak = 0;
                framesInPeak = 0;
                if (peaks.Count <= maximumPeaks) continue;
                Compact(peaks);
                currentFramesPerPeak *= 2;
            }
        }
        if (framesInPeak > 0) peaks.Add(currentPeak);
        while (peaks.Count > maximumPeaks) Compact(peaks);
        return new WaveformData(TimeSpan.FromSeconds(frameCount / (double)sampleRate), peaks.ToArray());
    }

    private static void Compact(List<float> peaks)
    {
        var write = 0;
        for (var read = 0; read < peaks.Count; read += 2)
            peaks[write++] = read + 1 < peaks.Count ? Math.Max(peaks[read], peaks[read + 1]) : peaks[read];
        peaks.RemoveRange(write, peaks.Count - write);
    }
}
