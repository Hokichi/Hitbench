using Hitbench.Resources.DataModels;

namespace Hitbench.Services;

internal sealed record OHsExportSchema(int[] Slots, OHsExportSchema? Previous, OHsExportRepresentation? Choice);
