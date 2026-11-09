namespace Hitbench.ViewModels.Shell;

public partial class WorkstationPageVM
{
    private bool isStaticMarker;
    public bool IsStaticMarker => isStaticMarker;
    public bool CanToggleScrollWithMarker => CanPlay && !IsStaticMarker;

    public void SetMarkerMode(bool value)
    {
        if (SetProperty(ref isStaticMarker, value, nameof(IsStaticMarker)))
            OnPropertyChanged(nameof(CanToggleScrollWithMarker));
    }
}
