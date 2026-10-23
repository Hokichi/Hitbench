using System.Collections.ObjectModel;
namespace Soundie.Resources.DataModels;

public sealed class InstrumentalTrackSnapshot(InstrumentalTrackContext context, string name, int trackVolume, IReadOnlyDictionary<string, string> keyFiles, IReadOnlyList<InstrumentalNoteSnapshot> notes, IReadOnlyList<string>? availableFiles, long revision, string folderName = "", IReadOnlyList<string>? reservedFolderNames = null, bool canUndo = false, bool canRedo = false, int trackColorIndex = 0, string? trackColor = null)
{
    public string? TrackColor { get; } = trackColor;
    public int TrackColorIndex { get; } = trackColorIndex;
    public bool CanUndo { get; } = canUndo;
    public bool CanRedo { get; } = canRedo;
    public string FolderName { get; } = folderName;
    public IReadOnlyList<string> ReservedFolderNames { get; } = Array.AsReadOnly((reservedFolderNames ?? []).ToArray());
    public InstrumentalTrackContext Context { get; } = context;
    public string Name { get; } = name;
    public int TrackVolume { get; } = trackVolume;
    public IReadOnlyDictionary<string, string> KeyFiles { get; } = new ReadOnlyDictionary<string, string>(new Dictionary<string, string>(keyFiles));
    public IReadOnlyList<InstrumentalNoteSnapshot> Notes { get; } = Array.AsReadOnly(notes.ToArray());
    public IReadOnlyList<string>? AvailableFiles { get; } = availableFiles is null ? null : Array.AsReadOnly(availableFiles.ToArray());
    public long Revision { get; } = revision;
}
