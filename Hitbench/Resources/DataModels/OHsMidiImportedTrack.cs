namespace Hitbench.Resources.DataModels;

public sealed class OHsMidiImportedTrack(string name, IReadOnlyList<OHsMidiImportedNote> notes)
{
    public string Name { get; } = name;
    public IReadOnlyList<OHsMidiImportedNote> Notes { get; } = Array.AsReadOnly(notes.ToArray());
}
