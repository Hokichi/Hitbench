using Hitbench.Resources.DataModels;
namespace Hitbench.Resources.Messages;

public sealed record InstrumentalEditResultMessage(InstrumentalTrackContext Context, Guid RequestId, bool Changed, Guid? SelectedNoteId, string? Error)
{
    private readonly IReadOnlyList<Guid>? selectedNoteIds;
    public IReadOnlyList<Guid>? SelectedNoteIds { get => selectedNoteIds; init => selectedNoteIds = value is null ? null : Array.AsReadOnly(value.ToArray()); }
}
