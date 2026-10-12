using System.Windows;
using osuHitsounder.Resources.DataModels;
namespace osuHitsounder.Resources.CustomControls;

public sealed class InstrumentalNoteEditRequestedEventArgs(RoutedEvent routedEvent, InstrumentalNoteEditKind kind, Guid noteId, string key, double milliseconds) : RoutedEventArgs(routedEvent)
{
    public InstrumentalNoteEditKind Kind { get; } = kind;
    public Guid NoteId { get; } = noteId;
    public string Key { get; } = key;
    public double RequestedMilliseconds { get; } = milliseconds;
}
