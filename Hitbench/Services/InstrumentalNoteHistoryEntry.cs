using Hitbench.Resources.DataModels;
namespace Hitbench.Services;

public sealed class InstrumentalNoteHistoryEntry(IReadOnlyList<InstrumentalNoteSnapshot> before, IReadOnlyList<InstrumentalNoteSnapshot> after, IReadOnlyList<Guid> affectedIds)
{
    public static long EstimateRetainedBytes(int beforeCount, int afterCount, int affectedCount) =>
        checked(256L + 96L * (beforeCount + (long)afterCount) + 16L * affectedCount);
    public IReadOnlyList<InstrumentalNoteSnapshot> Before { get; } = Array.AsReadOnly(before.ToArray());
    public IReadOnlyList<InstrumentalNoteSnapshot> After { get; } = Array.AsReadOnly(after.ToArray());
    public IReadOnlyList<Guid> AffectedNoteIds { get; } = Array.AsReadOnly(affectedIds.ToArray());
}
