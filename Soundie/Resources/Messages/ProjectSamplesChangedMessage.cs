using Soundie.Resources.DataModels;

namespace Soundie.Resources.Messages;

public sealed record ProjectSamplesChangedMessage
{
    public ProjectSamplesChangedMessage(SampleManagementContext context, IReadOnlyList<string> files,
        string? oldFileName = null, string? newFileName = null, bool includesSubfolders = true)
    { Context = context; Files = Array.AsReadOnly(files.ToArray()); OldFileName = oldFileName; NewFileName = newFileName; IncludesSubfolders = includesSubfolders; }
    public bool IncludesSubfolders { get; }
    public SampleManagementContext Context { get; }
    public IReadOnlyList<string> Files { get; }
    public string? OldFileName { get; }
    public string? NewFileName { get; }
}
