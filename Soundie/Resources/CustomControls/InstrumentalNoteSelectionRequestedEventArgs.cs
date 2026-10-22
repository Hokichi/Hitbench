using System.Windows;
namespace Soundie.Resources.CustomControls;
public sealed class InstrumentalNoteSelectionRequestedEventArgs(RoutedEvent routedEvent,IReadOnlyList<Guid> ids,Guid? anchorId):RoutedEventArgs(routedEvent)
{
    public IReadOnlyList<Guid> NoteIds {get;}=Array.AsReadOnly(ids.ToArray());
    public Guid? AnchorId {get;}=anchorId;
}
