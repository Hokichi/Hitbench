using CommunityToolkit.Mvvm.ComponentModel;

namespace osuHitsounder.Resources.DataModels;

public sealed partial class ControlPointEntry(ControlPointSnapshot initial) : ObservableObject
{
    public Guid Id { get; } = initial.Id;
    private int timestamp = initial.Timestamp;
    public int Timestamp => timestamp;
    [ObservableProperty] private decimal volume = initial.Volume;
    [ObservableProperty] private string sampleset = initial.Sampleset;
    [ObservableProperty] private string? error;
    private ControlPointSnapshot accepted = initial;
    public bool IsDirty => Volume != accepted.Volume || Sampleset != accepted.Sampleset;
    public void ApplySnapshot(ControlPointSnapshot point)
    {
        var dirty = IsDirty;
        var acknowledged = Volume == point.Volume && Sampleset == point.Sampleset;
        SetProperty(ref timestamp, point.Timestamp, nameof(Timestamp));
        accepted = point;
        if (!dirty || acknowledged) { Volume = point.Volume; Sampleset = point.Sampleset; Error = null; }
    }
    public void SetError(string? value) => Error = value;
}
