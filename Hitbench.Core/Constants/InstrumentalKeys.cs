namespace Hitbench.Core.Constants;

public static class InstrumentalKeys
{
    public static IReadOnlyList<string> All { get; } = Array.AsReadOnly(Enumerable.Range(1, 9)
        .SelectMany(octave => new[] { "C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B" }.Select(p => p + octave)).ToArray());
    public static bool TryGetIndex(string key, out int index)
    {
        index = -1;
        for (var i = 0; i < All.Count; i++) if (All[i] == key) { index = i; return true; }
        return false;
    }
    public static string GetName(int index) => All[index];
}
