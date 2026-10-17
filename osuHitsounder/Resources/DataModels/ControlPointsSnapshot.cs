namespace osuHitsounder.Resources.DataModels;

public sealed class ControlPointsSnapshot(ControlPointsContext context, IReadOnlyList<ControlPointSnapshot> points,
    long version, bool canAdd, string? unavailableReason, bool isSaving, string? saveError, long revision, bool canEdit)
{
    public ControlPointsContext Context { get; } = context;
    public IReadOnlyList<ControlPointSnapshot> Points { get; } = Array.AsReadOnly(points.ToArray());
    public long Version { get; } = version;
    public long Revision { get; } = revision;
    public bool CanEdit { get; } = canEdit;
    public bool CanAdd { get; } = canAdd;
    public string? UnavailableReason { get; } = unavailableReason;
    public bool IsSaving { get; } = isSaving;
    public string? SaveError { get; } = saveError;
}
