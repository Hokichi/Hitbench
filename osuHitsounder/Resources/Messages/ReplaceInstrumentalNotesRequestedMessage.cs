using osuHitsounder.Resources.DataModels;
namespace osuHitsounder.Resources.Messages;

public sealed class ReplaceInstrumentalNotesRequestedMessage(InstrumentalTrackContext context, Guid requestId, long expectedRevision, IReadOnlyList<InstrumentalNoteSnapshot> desiredNotes, IReadOnlyList<Guid> affectedNoteIds)
{
    public InstrumentalTrackContext Context { get; } = context;
    public Guid RequestId { get; } = requestId;
    public long ExpectedRevision { get; } = expectedRevision;
    public IReadOnlyList<InstrumentalNoteSnapshot> DesiredNotes { get; } = Array.AsReadOnly(desiredNotes.ToArray());
    public IReadOnlyList<Guid> AffectedNoteIds { get; } = Array.AsReadOnly(affectedNoteIds.ToArray());
}
