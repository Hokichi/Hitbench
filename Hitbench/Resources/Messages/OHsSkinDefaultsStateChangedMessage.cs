using Hitbench.Resources.DataModels;
namespace Hitbench.Resources.Messages;
public sealed record OHsSkinDefaultsStateChangedMessage
{
    public SampleManagementContext Context { get; }
    public bool AllowDefaults { get; }
    public string CurrentSkin { get; }
    public IReadOnlyList<string> Files { get; }
    public bool IsBusy { get; }
    public OHsSkinDefaultsStateChangedMessage(SampleManagementContext context, bool allowDefaults, string currentSkin, IReadOnlyList<string> files, bool isBusy)
    { Context = context; AllowDefaults = allowDefaults; CurrentSkin = currentSkin; Files = Array.AsReadOnly(files.ToArray()); IsBusy = isBusy; }
}
