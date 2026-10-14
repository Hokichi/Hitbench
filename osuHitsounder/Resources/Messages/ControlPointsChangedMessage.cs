using osuHitsounder.Resources.DataModels;
namespace osuHitsounder.Resources.Messages;

public sealed record ControlPointsChangedMessage(ControlPointsSnapshot Snapshot);
