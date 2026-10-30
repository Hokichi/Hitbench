using Hitbench.Resources.DataModels;
namespace Hitbench.ViewModels.Shell;

public partial class WorkstationPageVM
{
    public bool CanAcceptSampleTrackDrop(OHsSampleTrackDragData data) => CanAddTrack && !importBusy && !removalBusy && !instrumentBusy && !folderRenamesActive && !IsNodeEditBusy && data.Context.ProjectPath == projectPath && data.Context.SessionId == sampleSession && sampleTrackFiles.Contains(data.FileName, StringComparer.OrdinalIgnoreCase);
    public bool TryAddSampleTrack(OHsSampleTrackDragData data)
    {
        if (!CanAcceptSampleTrackDrop(data)) return false;
        AddSampleTrackCore(sampleTrackFiles.First(n => string.Equals(n, data.FileName, StringComparison.OrdinalIgnoreCase))); return true;
    }
}
