using System.Text;
using Hitbench.Core.Entities;
using Hitbench.Resources.DataModels;
using Hitbench.Services;
using Hitbench.Services.ProjectFiles;
namespace Hitbench.Test.Services;

public class OHsExportEncodingTests
{
    public static IEnumerable<TestCaseData> Encodings()
    {
        Encoding[] encodings = [new UTF8Encoding(false, true), new UTF8Encoding(true, true), new UnicodeEncoding(false, true, true), new UnicodeEncoding(true, true, true), new UTF32Encoding(false, true, true), new UTF32Encoding(true, true, true)];
        foreach (var encoding in encodings)
            foreach (var destination in new[] { OHsExportDestination.Project, OHsExportDestination.Beatmap })
                yield return new TestCaseData(encoding, destination);
    }
    [TestCaseSource(nameof(Encodings))]
    public async Task PreservesSourceEncodingAndUnicode(Encoding encoding, OHsExportDestination destination)
    {
        using var folder = new TestProjectFolder();
        var root = Path.GetDirectoryName(folder.ProjectPath)!;
        var path = Path.Combine(root, "Song [Original].osu");
        const string source = "osu file format v14\n\n[General]\nAudioFilename: 音楽.mp3\n[Metadata]\nTitle:한국어\nVersion: Original\n[Difficulty]\nHPDrainRate:7\nCircleSize:4\nOverallDifficulty:8.4\nApproachRate:9.6\nSliderMultiplier:2.3\n[TimingPoints]\n0,500,4,1,1,100,1,0\n\n[HitObjects]\nold\n[Unknown]\n// 終わり";
        var bytes = encoding.GetPreamble().Concat(encoding.GetBytes(source)).ToArray();
        await File.WriteAllBytesAsync(path, bytes);
        var request = new OHsExportRequest(Guid.NewGuid(), folder.ProjectPath, path, "unused", ProjectDataSerializer.Serialize(new ProjectData()), new("輸出 $1", 1, ".wav", ".wav", false, destination));
        var output = await new OHsExportService().ExportAsync(request, default);
        var expected = source.Replace("Version: Original", "Version: 輸出 $1").Replace("HPDrainRate:7", "HPDrainRate:3").Replace("CircleSize:4", "CircleSize:3").Replace("OverallDifficulty:8.4", "OverallDifficulty:5").Replace("ApproachRate:9.6", "ApproachRate:9.7").Replace("old\n", "");
        Assert.That(await File.ReadAllBytesAsync(Path.Combine(output, "Song [輸出 $1].osu")), Is.EqualTo(encoding.GetPreamble().Concat(encoding.GetBytes(expected)).ToArray()));
        Assert.That(await File.ReadAllBytesAsync(path), Is.EqualTo(bytes));
    }
    [TestCase(new byte[] { 0xff })]
    [TestCase(new byte[] { 0xff, 0xfe, 0x00 })]
    [TestCase(new byte[] { 0xff, 0xfe, 0x00, 0x00, 0xff })]
    [TestCase(new byte[] { 0xff, 0xfe, 0x00, 0xd8 })]
    [TestCase(new byte[] { 0x00, 0x00, 0xfe, 0xff, 0x00, 0x11, 0x00, 0x00 })]
    public async Task MalformedEncodingPreservesPreviousPublication(byte[] bytes)
    {
        using var folder = new TestProjectFolder();
        var root = Path.GetDirectoryName(folder.ProjectPath)!;
        var path = Path.Combine(root, "Song [Original].osu");
        await File.WriteAllBytesAsync(path, bytes);
        var output = await new Hitbench.Services.FileSystem.OHsExportPublisher().PublishAsync(folder.ProjectPath,
            new Dictionary<string, string> { ["keep.txt"] = "difficulty" },
            (stage, token) => File.WriteAllTextAsync(Path.Combine(stage, "keep.txt"), "keep", token), default);
        var manifest = await File.ReadAllBytesAsync(Path.Combine(root, ".osuhitsounder-export.json"));
        var request = new OHsExportRequest(Guid.NewGuid(), folder.ProjectPath, path, "unused", ProjectDataSerializer.Serialize(new ProjectData()), new("Hitsounds", 1, ".wav", ".wav", false));
        Assert.ThrowsAsync<DecoderFallbackException>(() => new OHsExportService().ExportAsync(request, default));
        Assert.That(await File.ReadAllTextAsync(Path.Combine(output, "keep.txt")), Is.EqualTo("keep"));
        Assert.That(await File.ReadAllBytesAsync(Path.Combine(root, ".osuhitsounder-export.json")), Is.EqualTo(manifest));
        Assert.That(Directory.GetDirectories(root, ".Export-staging-*"), Is.Empty);
    }
}
