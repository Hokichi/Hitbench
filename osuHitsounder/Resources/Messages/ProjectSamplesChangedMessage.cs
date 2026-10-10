using osuHitsounder.Resources.DataModels;

namespace osuHitsounder.Resources.Messages;

public sealed record ProjectSamplesChangedMessage
{
    public ProjectSamplesChangedMessage(SampleManagementContext context, IReadOnlyList<string> files,
        string? oldFileName = null, string? newFileName = null)
    { Context = context; Files = Array.AsReadOnly(files.ToArray()); OldFileName = oldFileName; NewFileName = newFileName; }
    public SampleManagementContext Context { get; }
    public IReadOnlyList<string> Files { get; }
    public string? OldFileName { get; }
    public string? NewFileName { get; }
}
