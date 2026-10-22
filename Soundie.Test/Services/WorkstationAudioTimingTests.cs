using NAudio.Wave;
using Soundie.Core.Entities;
using Soundie.Services;

namespace Soundie.Test.Services;

public class WorkstationAudioTimingTests
{
    private static readonly int[] Rates = [8000, 11025, 12000, 16000, 22050, 24000, 32000, 44100, 48000, 88200, 96000];
    private static string Fixture(string name) => Path.Combine(TestContext.CurrentContext.TestDirectory, "Fixtures", "Audio", name);

    [TestCase(1000, 44100)]
    [TestCase(1001, 44144)]
    [TestCase(1005, 44321)]
    public async Task PreparedSampleAlignsWithSongImpulse(int timestamp, int expectedFrame)
    {
        Func<string, WaveStream> decoder = path => path.EndsWith("sample.wav", StringComparison.Ordinal)
            ? Signal(44100, 0, 1, false) : Signal(44100, expectedFrame, 0, true);
        var reference = await Render(decoder, "song.wav", "sample.wav", timestamp);
        Assert.Multiple(() =>
        {
            Assert.That(Peak(reference, 0), Is.EqualTo(expectedFrame));
            Assert.That(Peak(reference, 1), Is.EqualTo(expectedFrame));
            Assert.That(Onset(reference, 0), Is.EqualTo(expectedFrame));
            Assert.That(Onset(reference, 1), Is.EqualTo(expectedFrame));
        });
        foreach (var size in new[] { 2, 512 })
            Assert.That(await Render(decoder, "song.wav", "sample.wav", timestamp, size), Is.EqualTo(reference));
    }

    private static IEnumerable<object[]> RateCases()
    {
        var pairs = Rates.SelectMany(rate => new[] { (44100, rate), (rate, 44100), (rate, rate) })
            .Append((48000, 96000)).Append((96000, 48000)).Distinct();
        foreach (var (song, sample) in pairs)
        foreach (var padded in new[] { false, true }) yield return [song, sample, padded];
    }

    [TestCaseSource(nameof(RateCases))]
    public async Task ResampledSampleAlignmentIsMeasured(int songRate, int sampleRate, bool padded)
    {
        var sampleFrame = padded ? (int)Math.Round(sampleRate / 10d, MidpointRounding.AwayFromZero) : 0;
        var songFrame = (int)Math.Round((1 + sampleFrame / (double)sampleRate) * songRate, MidpointRounding.AwayFromZero);
        Func<string, WaveStream> decoder = path => path.EndsWith("sample.wav", StringComparison.Ordinal)
            ? Signal(sampleRate, sampleFrame, 1, false) : Signal(songRate, songFrame, 0, true);
        var output = await Render(decoder, "song.wav", "sample.wav", 1000);
        Report($"PCM {songRate}/{sampleRate} padded={padded}", output);
        var songAlone = await Render(decoder, "song.wav", null, 0);
        var sampleAlone = await Render(decoder, "sample.wav", null, 0);
        TestContext.Out.WriteLine($"SOURCE song={songFrame}/{songRate}, sample={sampleFrame}/{sampleRate}; CONVERTED song={Peak(songAlone, 0)}, sample={Peak(sampleAlone, 1)}");
        Assert.Multiple(() =>
        {
            Assert.That(Peak(output, 1), Is.EqualTo(44100 + Peak(sampleAlone, 1)), "Scheduled placement after conversion");
            Assert.That(Peak(output, 0), Is.EqualTo(Peak(songAlone, 0)));
            Assert.That(Onset(output, 1), Is.EqualTo(44100 + Onset(sampleAlone, 1)), "Scheduled placement preserves converted onset including resampler delay");
        });
    }

    private static IEnumerable<object[]> EncodedCases()
    {
        var pairs = new HashSet<(string Song, string Sample)>();
        foreach (var padded in new[] { false, true })
        {
            var suffix = padded ? "100" : "0";
            foreach (var rate in Rates)
            foreach (var format in Formats(rate))
            {
                var song = Name(rate, "song" + suffix, format);
                var sample = Name(rate, "sample" + suffix, format);
                pairs.Add((Name(44100, "song" + suffix, "wav"), sample));
                pairs.Add((song, Name(44100, "sample" + suffix, "wav")));
                pairs.Add((song, sample));
            }
            foreach (var rate in new[] { 44100, 48000 })
            foreach (var songFormat in new[] { "wav", "ogg", "mp3" })
            foreach (var sampleFormat in new[] { "wav", "ogg", "mp3" })
                pairs.Add((Name(rate, "song" + suffix, songFormat), Name(rate, "sample" + suffix, sampleFormat)));
            pairs.Add((Name(96000, "song" + suffix, "ogg"), Name(48000, "sample" + suffix, "mp3")));
            pairs.Add((Name(48000, "song" + suffix, "mp3"), Name(96000, "sample" + suffix, "ogg")));
        }
        return pairs.Select(pair => new object[] { pair.Song, pair.Sample });
    }

    [TestCaseSource(nameof(EncodedCases))]
    public async Task EncodedSampleTimingIsMeasured(string songName, string sampleName)
    {
        var songPath = Fixture(songName);
        var samplePath = Fixture(sampleName);
        ReportNative(songPath, 0);
        ReportNative(samplePath, 1);
        var songAlone = await Render(AudioDecoder.Open, songPath, null, 0);
        var sampleAlone = await Render(AudioDecoder.Open, samplePath, null, 0);
        var output = await Render(AudioDecoder.Open, songPath, samplePath, 1000);
        Report($"ENCODED {songName}/{sampleName}", output);
        TestContext.Out.WriteLine($"CONVERTED song peak={Peak(songAlone, 0)}, onset={Onset(songAlone, 0)}; sample peak={Peak(sampleAlone, 1)}, onset={Onset(sampleAlone, 1)}");
        Assert.Multiple(() =>
        {
            Assert.That(Peak(output, 1), Is.EqualTo(44100 + Peak(sampleAlone, 1)));
            Assert.That(Onset(output, 1), Is.EqualTo(44100 + Onset(sampleAlone, 1)));
            Assert.That(Peak(output, 0), Is.EqualTo(Peak(songAlone, 0)));
        });
        // Compare the whole triggered signal, including silence and codec padding, against its independent conversion.
        var expected = new float[output.Length];
        for (var i = 0; i < output.Length; i += 2)
        {
            expected[i] = songAlone[i];
            var sampleIndex = i - 88200 + 1;
            if (sampleIndex >= 0 && sampleIndex < sampleAlone.Length) expected[i + 1] = sampleAlone[sampleIndex];
        }
        Assert.That(output, Is.EqualTo(expected).Within(1e-6));
    }

    private static IEnumerable<object[]> LifecycleCases()
    {
        foreach (var rate in new[] { 44100, 48000, 88200, 96000 })
        foreach (var seek in new[] { 750, 1000 }) yield return [rate, "pcm", seek];
        foreach (var format in new[] { "wav", "ogg", "mp3", "no-xing.mp3" })
        foreach (var seek in new[] { 750, 1000 }) yield return [48000, format, seek];
    }

    [TestCaseSource(nameof(LifecycleCases))]
    public async Task SeekPreservesImpulseAlignment(int rate, string format, int seek)
    {
        var (decoder, song, sample) = Source(rate, format);
        var reference = await Render(decoder, song, sample, 1000);
        var output = await Render(decoder, song, sample, 1000, 512, seek);
        Assert.That(output, Is.EqualTo(reference[(seek == 750 ? 66150 : 88200)..]));
        if (rate == 44100 && format == "pcm")
            Assert.That(Peak(output, 1), Is.EqualTo(seek == 750 ? 11025 : 0));
    }

    [TestCase(44100, "pcm")]
    [TestCase(96000, "pcm")]
    [TestCase(96000, "ogg")]
    [TestCase(48000, "mp3")]
    public async Task PauseResumePreservesImpulseAlignment(int rate, string format)
    {
        var (decoder, song, sample) = Source(rate, format);
        var reference = await Render(decoder, song, sample, 1000);
        Assert.That(await Render(decoder, song, sample, 1000, pause: true), Is.EqualTo(reference));
    }

    private static (Func<string, WaveStream> Decoder, string Song, string Sample) Source(int rate, string format) => format == "pcm"
        ? (path => path.EndsWith("sample.wav", StringComparison.Ordinal) ? Signal(rate, 0, 1, false) : Signal(rate, rate, 0, true), "song.wav", "sample.wav")
        : (AudioDecoder.Open, Fixture(Name(rate, "song0", format)), Fixture(Name(rate, "sample0", format)));

    private static string[] Formats(int rate) => rate <= 48000 ? ["wav", "ogg", "mp3", "no-xing.mp3"] : ["wav", "ogg"];
    private static string Name(int rate, string role, string format) => format == "no-xing.mp3"
        ? $"timing-{rate}-{role}-no-xing.mp3" : $"timing-{rate}-{role}.{format}";

    private static void ReportNative(string path, int channel)
    {
        using var stream = AudioDecoder.Open(path);
        var rate = stream.WaveFormat.SampleRate;
        Assert.That(rate, Is.EqualTo(int.Parse(Path.GetFileName(path).Split('-')[1])));
        var samples = ReadAll(stream.ToSampleProvider(), 8192);
        var padded = Path.GetFileNameWithoutExtension(path).Split('-')[2].EndsWith("100", StringComparison.Ordinal);
        var originalFrame = Math.Round(((channel == 0 ? 1 : 0) + (padded ? .1 : 0)) * rate, MidpointRounding.AwayFromZero);
        TestContext.Out.WriteLine($"NATIVE {Path.GetFileName(path)} rate={rate} original={originalFrame}, peak={Peak(samples, channel)}, onset={Onset(samples, channel)}, peakDeltaMs={(Peak(samples, channel) - originalFrame) * 1000 / rate:F6}");
    }

    private static async Task<float[]> Render(Func<string, WaveStream> decoder, string song, string? sample,
        int timestamp, int chunk = 8192, int? seek = null, bool pause = false)
    {
        using var project = new TestProjectFolder(sample is null ? [] : [Path.GetFileName(sample)]);
        if (sample is not null && File.Exists(sample))
            File.Copy(sample, Path.Combine(Path.GetDirectoryName(project.ProjectPath)!, "Samples", Path.GetFileName(sample)), true);
        var player = new StubWavePlayer();
        using var service = new WorkstationAudioService(decoder, () => player);
        service.Open(song);
        await service.UpdateSamplesAsync(project.ProjectPath, sample is null ? [] : [new SamplePlaybackEvent(1, Path.GetFileName(sample), timestamp, 100)], CancellationToken.None);
        Assert.That(service.GetSnapshot().Error, Is.Null);
        if (seek is { } milliseconds) service.Seek(TimeSpan.FromMilliseconds(milliseconds));
        service.Play();
        Assert.That(service.GetSnapshot().Error, Is.Null);
        var provider = player.Provider!.ToSampleProvider();
        Assert.That(provider.WaveFormat.SampleRate, Is.EqualTo(44100));
        Assert.That(provider.WaveFormat.Channels, Is.EqualTo(2));
        // Ordinary reads cross sample onsets without marker delivery or boundary splitting.
        var output = new List<float>();
        var buffer = new float[chunk];
        var position = (int)Math.Round((seek ?? 0) / 1000d * 44100) * 2;
        var paused = false;
        while (true)
        {
            if (pause && !paused && position >= 44100)
            {
                player.Position = position * sizeof(float);
                service.Pause();
                service.Play();
                provider = player.Provider!.ToSampleProvider();
                paused = true;
            }
            var read = provider.Read(buffer, 0, chunk);
            if (read == 0) break;
            output.AddRange(buffer.AsSpan(0, read).ToArray());
            position += read;
        }
        Assert.That(output, Is.Not.Empty);
        Assert.That(output.All(float.IsFinite), Is.True);
        return output.ToArray();
    }

    private static WaveStream Signal(int rate, int frame, int channel, bool song)
    {
        var samples = new float[(int)Math.Ceiling(rate * (song ? 1.4 : .3)) * 2];
        samples[frame * 2 + channel] = .5f;
        var bytes = new byte[samples.Length * sizeof(float)];
        Buffer.BlockCopy(samples, 0, bytes, 0, bytes.Length);
        return new RawSourceWaveStream(new MemoryStream(bytes), WaveFormat.CreateIeeeFloatWaveFormat(rate, 2));
    }

    private static float[] ReadAll(ISampleProvider provider, int chunk)
    {
        var output = new List<float>();
        var buffer = new float[chunk];
        int read;
        while ((read = provider.Read(buffer, 0, buffer.Length)) > 0) output.AddRange(buffer.AsSpan(0, read).ToArray());
        Assert.That(output, Is.Not.Empty);
        Assert.That(output.All(float.IsFinite), Is.True);
        return output.ToArray();
    }

    private static int Peak(float[] samples, int channel)
    {
        var peak = channel;
        for (var i = channel; i < samples.Length; i += 2)
            if (Math.Abs(samples[i]) > Math.Abs(samples[peak])) peak = i;
        Assert.That(Math.Abs(samples[peak]), Is.GreaterThan(.0001), "Signal must survive conversion");
        return peak / 2;
    }

    private static int Onset(float[] samples, int channel)
    {
        for (var i = channel; i < samples.Length; i += 2)
            if (Math.Abs(samples[i]) > .0001) return i / 2;
        Assert.Fail("No signal onset found");
        return -1;
    }

    private static void Report(string label, float[] output) => TestContext.Out.WriteLine(
        $"{label}: song peak={Peak(output, 0)}, onset={Onset(output, 0)}; sample peak={Peak(output, 1)}, onset={Onset(output, 1)}; deltaFrames={Peak(output, 1) - Peak(output, 0)}, deltaMs={(Peak(output, 1) - Peak(output, 0)) * 1000d / 44100:F6}");
}
