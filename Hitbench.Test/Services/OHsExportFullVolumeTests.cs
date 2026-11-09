using Hitbench.Core.Entities;
using Hitbench.Core.Enums;
using Hitbench.Resources.DataModels;
using Hitbench.Services;
using Hitbench.Services.ProjectFiles;

namespace Hitbench.Test.Services;

public class OHsExportFullVolumeTests
{
    [TestCase(false), TestCase(true)]
    public async Task HotSampleClipsBeforeTimingVolume(bool instrumental)
    {
        using var folder = new TestProjectFolder();
        var root = Path.GetDirectoryName(folder.ProjectPath)!;
        var map = Path.Combine(root, "Song [Original].osu");
        await File.WriteAllTextAsync(map, "[TimingPoints]\n0,500,4,1,1,100,1,0\n");
        using var source = DecodedAudio.CopyFrom(Enumerable.Range(0, 441).SelectMany(_ => new[] { .9f, -.9f }).ToArray());
        await OHsExportAudio.WriteFileAsync(Path.Combine(root, "Samples", "hot.wav"), source, default);
        Track track = instrumental
            ? new InstrumentalTrack { KeyFiles = new() { ["C4"] = "hot.wav" }, Notes = [new() { Key = "C4", Timestamp = 100 }] }
            : new Track { Samples = [new() { FileName = "hot.wav", Timestamp = 100 }] };
        track.Volume = 120; track.Addition = "Normal"; track.SampleType = SampleType.Normal;
        ControlPoint[][] controls = [[], [new() { Volume = 30 }], [new() { Volume = 0 }],
            [new() { Volume = 10, IsRamp = true }, new() { Timestamp = 1000, Volume = 50 }]];
        int[] volumes = [100, 30, 0, 14];
        for (var i = 0; i < controls.Length; i++)
        {
            var project = new ProjectData { Tracks = [track], ControlPoints = controls[i].ToList() };
            var request = new OHsExportRequest(Guid.NewGuid(), folder.ProjectPath, map, "unused.wav",
                ProjectDataSerializer.Serialize(project), new("Hitsounds", 1, ".wav", ".wav", false));
            using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var output = await new OHsExportService().ExportAsync(request, cancellation.Token);
            var files = Directory.GetFiles(output, "*.wav");
            Assert.That(files, Has.Length.EqualTo(1));
            using var rendered = AudioDecoder.Decode(files.Single(f => !instrumental || f.Contains("whistle")), default);
            Assert.That(rendered.Samples[0], Is.EqualTo(1).Within(2d / 32768));
            Assert.That(rendered.Samples[1], Is.EqualTo(-1).Within(2d / 32768));
            var text = await File.ReadAllTextAsync(Path.Combine(output, "Song [Hitsounds].osu"));
            Assert.That(OHsExportPlaybackOracle.Resolve(text).First().Volume, Is.EqualTo(volumes[i]));
            var pcm = OHsExportPlaybackOracle.Render(text, name => AudioDecoder.Decode(Path.Combine(output, name), default), 5000, _ => DecodedAudio.CopyFrom(new float[2]));
            Assert.That(pcm[4410 * 2], Is.EqualTo(volumes[i] / 100d).Within(2d / 32768));
            Assert.That(pcm[4410 * 2 + 1], Is.EqualTo(-volumes[i] / 100d).Within(2d / 32768));
        }
    }

    [Test]
    public async Task MixedRepeatedServiceExportUsesMinimumInventory()
    {
        using var folder = new TestProjectFolder();
        var root = Path.GetDirectoryName(folder.ProjectPath)!;
        var map = Path.Combine(root, "Song [Original].osu");
        await File.WriteAllTextAsync(map, "[TimingPoints]\n0,500,4,1,1,100,1,0\n");
        var voices = MixedExportVoices.Create(1400);
        foreach (var file in voices.Select(v => v.FileName).Distinct())
        {
            using var audio = DecodedAudio.CopyFrom([.1f, .1f]);
            await OHsExportAudio.WriteFileAsync(Path.Combine(root, "Samples", file), audio, default);
        }
        var project = new ProjectData
        {
            Tracks = voices.GroupBy(v => (v.GainPercent, v.Sampleset, v.Type, v.TrackOrder)).Select(g => new Track
            {
                Volume = g.Key.GainPercent, Addition = g.Key.Sampleset, SampleType = g.Key.Type,
                Samples = g.Select(v => new Sample { Timestamp = v.Timestamp, FileName = v.FileName }).ToList()
            }).ToList(),
            ControlPoints = [new() { Volume = 30 }, new() { Timestamp = 10000, Volume = 10, IsRamp = true }, new() { Timestamp = 20000, Volume = 80 }]
        };
        var request = new OHsExportRequest(Guid.NewGuid(), folder.ProjectPath, map, "unused.wav",
            ProjectDataSerializer.Serialize(project), new("Hitsounds", 1, ".wav", ".wav", false));
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var output = await new OHsExportService().ExportAsync(request, cancellation.Token);
        Assert.That(Directory.GetFiles(output, "*.wav"), Has.Length.EqualTo(10));
        var text = await File.ReadAllTextAsync(Path.Combine(output, "Song [Hitsounds].osu"));
        var hits = OHsExportPlaybackOracle.Resolve(text).GroupBy(h => h.Timestamp).ToArray();
        Assert.That(hits.Select(g => g.Key), Is.EqualTo(Enumerable.Range(0, 1400).Select(i => 1000 + i * 100)));
        var amplitudes = Directory.GetFiles(output, "*.wav").ToDictionary(file => Path.GetFileName(file)!, file =>
        {
            using var audio = AudioDecoder.Decode(file, default); return audio.Samples[0];
        });
        var expected = voices.GroupBy(v => v.Timestamp).ToDictionary(g => g.Key, g => g.Sum(v => .1 * v.GainPercent / 100));
        foreach (var group in hits)
        {
            var lookup = group.Key + 5m;
            var volume = lookup < 10000 ? 30 : lookup >= 20000 ? 80 : (int)Math.Round(10 + 70 * (lookup - 10000) / 10000, MidpointRounding.AwayFromZero);
            double actual = 0;
            foreach (var hit in group)
            {
                Assert.That(hit.Volume, Is.EqualTo(volume));
                var set = new[] { "normal", "soft", "drum" }[hit.Set - 1];
                var suffix = hit.Index == 1 ? "" : hit.Index.ToString();
                if (!amplitudes.TryGetValue($"{set}-hit{hit.Type}{suffix}.wav", out var amplitude))
                {
                    // Explicit silent test skin for native fallback; authored normals must still have files.
                    Assert.That(hit.Type, Is.EqualTo("normal"));
                    Assert.That(voices.Any(v => v.Timestamp == hit.Timestamp && v.Type == SampleType.Normal), Is.False);
                    amplitude = 0;
                }
                actual += amplitude * volume / 100d;
            }
            Assert.That(actual, Is.EqualTo(expected[group.Key] * volume / 100).Within(8d / 32768));
        }
    }
}
