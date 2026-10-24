using Hitbench.Core.Enums;
using Hitbench.Resources.DataModels;

namespace Hitbench.Test.Services;

internal static class MixedExportVoices
{
    internal static OHsExportVoice[] Create(int groupCount) => Enumerable.Range(0, groupCount).SelectMany(i =>
    {
        var p = i % 14;
        var time = 1000 + i * 100;
        var voices = new List<OHsExportVoice> { new(time, $"w{p % 2}.wav", 70, "Soft", SampleType.Whistle, 0) };
        if (p % 3 == 0) voices.Add(new(time, $"n{p % 3}.wav", 100, "Normal", SampleType.Normal, 1));
        if (p % 2 == 0) voices.Add(new(time, $"c{p % 3}.wav", 60, "Drum", SampleType.Clap, 2));
        if (p % 4 == 0) voices.Add(new(time, $"f{p % 2}.wav", 40, "Soft", SampleType.Finish, 3));
        return voices;
    }).ToArray();
}
