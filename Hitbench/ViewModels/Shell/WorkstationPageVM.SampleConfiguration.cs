using System.ComponentModel;
using Hitbench.Core.Entities;

namespace Hitbench.ViewModels.Shell;

public partial class WorkstationPageVM
{
    private Track? configuringSampleTrack;
    private Guid? sampleConfigurationOwner;
    public bool IsSampleConfigurationOpen => sampleConfigurationOwner is not null;
    public bool CanCreateSampleNodes => CanPlaceSamples && IsAdditionMode && !IsSampleConfigurationOpen;

    public Guid? BeginSampleConfiguration(Track track)
    {
        if (!CanEditTracks || track is InstrumentalTrack || !tracks.Contains(track)) return null;
        configuringSampleTrack = track;
        sampleConfigurationOwner = Guid.NewGuid();
        var owner = sampleConfigurationOwner;
        OnPropertyChanged(nameof(IsSampleConfigurationOpen));
        OnPropertyChanged(nameof(CanCreateSampleNodes));
        return owner;
    }

    internal bool OwnsSampleConfiguration(Guid owner) => sampleConfigurationOwner == owner;
    public void EndSampleConfiguration(Guid ownerToken)
    {
        if (sampleConfigurationOwner == ownerToken) ResetSampleConfiguration();
    }

    private void ResetSampleConfiguration()
    {
        configuringSampleTrack = null;
        sampleConfigurationOwner = null;
        OnPropertyChanged(nameof(IsSampleConfigurationOpen));
        OnPropertyChanged(nameof(CanCreateSampleNodes));
    }

    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.PropertyName == nameof(CanPlaceSamples)) OnPropertyChanged(nameof(CanCreateSampleNodes));
    }
}
