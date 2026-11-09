namespace Hitbench.ViewModels.Shell;

public partial class WorkstationPageVM
{
    private bool isStaticMarker;
    public bool IsStaticMarker => isStaticMarker;
    public bool CanToggleScrollWithMarker => CanPlay && !IsStaticMarker;

    public void SetMarkerMode(bool value)
    {
        if (IsStaticMarker == value) return;
        RestoreMarkerMode(value);
        if (Project is not null)
        {
            Project.IsMarkerStatic = value;
            QueueSave();
        }
    }

    private void RestoreMarkerMode(bool value)
    {
        if (SetProperty(ref isStaticMarker, value, nameof(IsStaticMarker)))
            OnPropertyChanged(nameof(CanToggleScrollWithMarker));
    }
}
