namespace Hitbench.Resources.DataModels;

public sealed record OHsSkinDefaultsSnapshot
{
    public string SkinName { get; }
    public IReadOnlyList<string> Files { get; }
    public bool WasReplaced { get; }

    public OHsSkinDefaultsSnapshot(string skinName, IReadOnlyList<string> files, bool wasReplaced)
    {
        SkinName = skinName;
        Files = Array.AsReadOnly(files.ToArray());
        WasReplaced = wasReplaced;
    }
}
