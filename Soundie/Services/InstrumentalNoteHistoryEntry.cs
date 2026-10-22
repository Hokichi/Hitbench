using Soundie.Resources.DataModels;
namespace Soundie.Services;

public sealed class InstrumentalNoteHistoryEntry(IReadOnlyList<InstrumentalNoteSnapshot> before, IReadOnlyList<InstrumentalNoteSnapshot> after, IReadOnlyList<Guid> affectedIds)
{
    public IReadOnlyList<InstrumentalNoteSnapshot> Before { get; } = Array.AsReadOnly(before.ToArray());
    public IReadOnlyList<InstrumentalNoteSnapshot> After { get; } = Array.AsReadOnly(after.ToArray());
    public IReadOnlyList<Guid> AffectedNoteIds { get; } = Array.AsReadOnly(affectedIds.ToArray());
}
