using Onset.Resources.DataModels;

namespace Onset.Services;

internal sealed record OHsExportSchema(int[] Slots, OHsExportSchema? Previous, OHsExportRepresentation? Choice);
