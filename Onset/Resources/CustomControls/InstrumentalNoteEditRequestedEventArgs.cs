using System.Windows;
using Onset.Resources.DataModels;
namespace Onset.Resources.CustomControls;

public sealed class InstrumentalNoteEditRequestedEventArgs(RoutedEvent routedEvent, InstrumentalNoteEditKind kind, Guid noteId, string key, double milliseconds, IReadOnlyList<Guid>? noteIds = null) : RoutedEventArgs(routedEvent)
{
    public IReadOnlyList<Guid> NoteIds {get;}=Array.AsReadOnly((noteIds ?? (noteId==Guid.Empty ? [] : new[]{noteId})).ToArray());
    public InstrumentalNoteEditKind Kind { get; } = kind;
    public Guid NoteId { get; } = noteId;
    public string Key { get; } = key;
    public double RequestedMilliseconds { get; } = milliseconds;
}
