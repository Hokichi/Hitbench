using System.Collections.ObjectModel;
namespace osuHitsounder.Resources.DataModels;
public sealed class InstrumentalTrackSnapshot(InstrumentalTrackContext context,string name,int trackVolume,IReadOnlyDictionary<string,string> keyFiles,IReadOnlyList<InstrumentalNoteSnapshot> notes,IReadOnlyList<string>? availableFiles,long revision)
{
 public InstrumentalTrackContext Context {get;}=context;
 public string Name {get;}=name;
 public int TrackVolume {get;}=trackVolume;
 public IReadOnlyDictionary<string,string> KeyFiles {get;}=new ReadOnlyDictionary<string,string>(new Dictionary<string,string>(keyFiles));
 public IReadOnlyList<InstrumentalNoteSnapshot> Notes {get;}=Array.AsReadOnly(notes.ToArray());
 public IReadOnlyList<string>? AvailableFiles {get;}=availableFiles is null ? null : Array.AsReadOnly(availableFiles.ToArray());
 public long Revision {get;}=revision;
}
