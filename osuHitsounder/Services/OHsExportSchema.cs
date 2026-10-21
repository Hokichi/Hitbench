using osuHitsounder.Resources.DataModels;

namespace osuHitsounder.Services;

internal sealed record OHsExportSchema(int[] Slots, OHsExportSchema? Previous, OHsExportRepresentation? Choice);
