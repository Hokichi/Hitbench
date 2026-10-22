using Soundie.Resources.DataModels;

namespace Soundie.Services;

internal sealed record OHsExportSchema(int[] Slots, OHsExportSchema? Previous, OHsExportRepresentation? Choice);
