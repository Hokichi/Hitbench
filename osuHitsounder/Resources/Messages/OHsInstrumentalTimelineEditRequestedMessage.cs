using osuHitsounder.Resources.DataModels;

namespace osuHitsounder.Resources.Messages;

public sealed record OHsInstrumentalTimelineEditRequestedMessage(InstrumentalTrackContext Context,
    long Sequence, double? ZoomFactor = null, int? SelectedSubdivisionIndex = null,
    bool? ScrollWithMarker = null, bool? IsAdditionMode = null);
